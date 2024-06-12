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
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "BroadcastHoundMonitor/1.0 (+https://www.broadcasthound.com)");

            InitializeFFmpeg().Wait();
        }

        // private async Task InitializeFFmpeg()
        // {
        //     _logger.LogInformation("Initializing FFmpeg.");
        //     var ffmpegPath = AppDomain.CurrentDomain.BaseDirectory;
        //     _logger.LogInformation($"FFmpeg path: {ffmpegPath}");
        //     FFmpeg.SetExecutablesPath(ffmpegPath);
        // }
        //
        private async Task InitializeFFmpeg()
        {
            _logger.LogInformation("Initializing FFmpeg.");
            // Set the path to the directory containing ffmpeg and ffprobe
            var ffmpegPath = "/usr/bin";
            _logger.LogInformation($"FFmpeg path: {ffmpegPath}");
            FFmpeg.SetExecutablesPath(ffmpegPath);
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
                while (totalBytesRead < maxBytesToRead && (bytesRead = await responseStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
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

                await InsertCheckAsync(stream, db, status);
            }
            catch (HttpRequestException httpEx)
            {
                _logger.LogError(httpEx, "Network error while monitoring stream {url}", stream.Url);
                await InsertCheckAsync(stream, 0, "down");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error monitoring stream {url}", stream.Url);
                await InsertCheckAsync(stream, 0, "down");
            }
        }

        private double GetVolumeFromLog(string log)
        {
            var match = Regex.Match(log, @"max_volume: (?<volume>[-\d\.]+) dB");
            return match.Success ? double.Parse(match.Groups["volume"].Value) : 0.0;
        }

        private async Task InsertCheckAsync(Stream stream, double volume, string status)
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
                var response = await _supabaseClient.From<Check>().Insert(check);
                if (response.Models.Count > 0)
                {
                    _logger.LogInformation("Check inserted successfully for {url}", stream.Url);
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
    }
}
