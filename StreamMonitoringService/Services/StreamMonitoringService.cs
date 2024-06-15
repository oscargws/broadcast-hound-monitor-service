using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Supabase;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xabe.FFmpeg;

namespace StreamMonitoringService
{
    public class StreamMonitoringService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<StreamMonitoringService> _logger;
        private readonly Supabase.Client _supabaseClient;
        private readonly HttpClient _httpClient;
        private string _ffmpegLog;

        public StreamMonitoringService(IConfiguration configuration, ILogger<StreamMonitoringService> logger)
        {
            _configuration = configuration;
            _logger = logger;
            _supabaseClient = new Supabase.Client(_configuration["Supabase:Url"], _configuration["Supabase:Key"]);
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent",
                "tonearm-agent/1.0 (+https://www.usetonearm.com)");

            InitializeFFmpeg().Wait();
        }

        private async Task InitializeFFmpeg()
        {
            if (_configuration["Environment"] == "Local")
            {
                _logger.LogInformation("Running local environment");
                _logger.LogInformation("Initializing FFmpeg.");

                var ffmpegPath = AppDomain.CurrentDomain.BaseDirectory;
                _logger.LogInformation($"FFmpeg path: {ffmpegPath}");
                FFmpeg.SetExecutablesPath(ffmpegPath);
            }
            else
            {
                _logger.LogInformation("Running prod");
                _logger.LogInformation("Initializing FFmpeg.");

                var ffmpegPath = "/usr/bin";
                _logger.LogInformation($"FFmpeg path: {ffmpegPath}");
                FFmpeg.SetExecutablesPath(ffmpegPath);
            }
        }

        public async Task FetchAndMonitorStreamsAsync()
        {
            var streams = await FetchStreamsAsync();
            var tasks = streams.Select(stream => MonitorStreamAsync(stream));
            await Task.WhenAll(tasks);
        }

        private async Task<IEnumerable<Stream>> FetchStreamsAsync()
        {
            _logger.LogInformation("Fetching streams from Supabase.");
            var response = await _supabaseClient.From<Stream>().Get();
            var streams = response.Models;
            _logger.LogInformation($"Fetched {streams.Count()} streams.");
            return streams;
        }

        private async Task MonitorStreamAsync(Stream stream)
        {
            try
            {
                _logger.LogInformation("Monitoring stream: {url}", stream.Url);
                var response = await _httpClient.GetAsync(stream.Url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                using var streamData = new MemoryStream();
                var buffer = new byte[4096];
                int bytesRead;
                int totalBytesRead = 0;
                int maxBytesToRead = 5 * 44100 * 2; // 5 seconds of audio at 44.1kHz, 16-bit PCM (stereo)

                using var responseStream = await response.Content.ReadAsStreamAsync();
                while (totalBytesRead < maxBytesToRead &&
                       (bytesRead = await responseStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    streamData.Write(buffer, 0, bytesRead);
                    totalBytesRead += bytesRead;
                }

                streamData.Position = 0;

                var tempFilePath = Path.GetTempFileName();
                await File.WriteAllBytesAsync(tempFilePath, streamData.ToArray());

                var mediaInfo = await FFmpeg.GetMediaInfo(tempFilePath);
                var audioStream = mediaInfo.AudioStreams.First();

                var outputFilePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".aac");
                var conversion = FFmpeg.Conversions.New()
                    .AddStream(audioStream)
                    .AddParameter("-af volumedetect")
                    .SetOutput(outputFilePath);

                _ffmpegLog = string.Empty;
                conversion.OnDataReceived += (sender, args) => _ffmpegLog += args.Data;

                await conversion.Start();

                var db = GetVolumeFromLog(_ffmpegLog);

                var status = db < -30 ? "down" : "online";
                _logger.LogInformation("Stream {url} is {status} with volume {db} dB", stream.Url, status, db);

                await InsertCheckAndUpdateStreamAsync(stream, db, status);
            }
            catch (HttpRequestException httpEx)
            {
                _logger.LogError(httpEx, "Network error while monitoring stream {url}", stream.Url);
                await InsertCheckAndUpdateStreamAsync(stream, 0, "down");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error monitoring stream {url}", stream.Url);
                await InsertCheckAndUpdateStreamAsync(stream, 0, "down");
            }
        }

        private double GetVolumeFromLog(string log)
        {
            var match = Regex.Match(log, @"max_volume: (?<volume>[-\d\.]+) dB");
            return match.Success ? double.Parse(match.Groups["volume"].Value) : 0.0;
        }

        private async Task InsertCheckAndUpdateStreamAsync(Stream stream, double volume, string status)
        {
            _logger.LogInformation("Inserting check into database for {url}", stream.Url);

            var check = new Check
            {
                Id = Guid.NewGuid(),
                Completed = true,
                Stream = stream.Id,
                Status = status,
                AccountId = stream.AccountId
            };

            try
            {
                var checkResponse = await _supabaseClient.From<Check>().Insert(check);
                if (checkResponse.Models.Count > 0)
                {
                    _logger.LogInformation("Check inserted successfully for {url}", stream.Url);

                    // Update the stream with the new information
                    await UpdateStreamAsync(stream, status);
                }
                else
                {
                    _logger.LogError("Failed to insert check for {url}. No records inserted.", stream.Url);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to insert check for {url}", stream.Url);
            }
        }

        private async Task UpdateStreamAsync(Stream stream, string status)
        {
            _logger.LogInformation("Updating stream information for {url}", stream.Url);

            try
            {
                if (status == "online")
                {
                    var update = await _supabaseClient.From<Stream>()
                        .Where(x => x.Id == stream.Id)
                        .Set(x => x.LastCheck, DateTime.Now)
                        .Set(x => x.Status, status)
                        .Set(x => x.LastOnline, DateTime.Now)
                        .Update();
                }
                else if (status == "error" || status == "down" || status == "silence")
                {
                    var update = await _supabaseClient.From<Stream>()
                        .Where(x => x.Id == stream.Id)
                        .Set(x => x.LastCheck, DateTime.Now)
                        .Set(x => x.Status, status)
                        .Set(x => x.LastOutage, DateTime.Now)
                        .Update();
                }

                _logger.LogInformation("Stream updated successfully for {url}", stream.Url);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update stream for {url}", stream.Url);
            }
        }
    }
}