using Amazon.SQS;
using Amazon.SQS.Model;
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
using Xabe.FFmpeg.Downloader;

namespace StreamMonitoringService
{
    public class StreamMonitoringService
    {
        private readonly IConfiguration _configuration;
        private readonly IAmazonSQS _sqsClient;
        private readonly ILogger<StreamMonitoringService> _logger;
        private readonly Supabase.Client _supabaseClient;
        private string _ffmpegLog;

        public StreamMonitoringService(IConfiguration configuration, IAmazonSQS sqsClient, ILogger<StreamMonitoringService> logger)
        {
            _configuration = configuration;
            _sqsClient = sqsClient;
            _logger = logger;
            _supabaseClient = new Supabase.Client(_configuration["Supabase:Url"], _configuration["Supabase:Key"]);

            InitializeFFmpeg().Wait();
        }

        private async Task InitializeFFmpeg()
        {
            _logger.LogInformation("Looking for ffmpegPath in path:");
            // var ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg-lib");
            var ffmpegPath = AppDomain.CurrentDomain.BaseDirectory;
            _logger.LogInformation(ffmpegPath);
            FFmpeg.SetExecutablesPath(AppDomain.CurrentDomain.BaseDirectory);
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
            _logger.LogInformation($"Fetched {streams.Count} streams.");
            return streams;
        }

        private async Task MonitorStreamAsync(Stream stream)
        {
            try
            {
                _logger.LogInformation("Monitoring stream: {url}", stream.Url);
                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Add("User-Agent", "BroadcastHoundMonitor/1.0 (+https://www.broadcasthound.com)");
                var response = await httpClient.GetAsync(stream.Url, HttpCompletionOption.ResponseHeadersRead);
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

                await SendMessageToSQSAsync(stream, db, status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error monitoring stream {url}", stream.Url);
                await SendMessageToSQSAsync(stream, 0, "down");
            }
        }

        private double GetVolumeFromLog(string log)
        {
            var match = Regex.Match(log, @"max_volume: (?<volume>[-\d\.]+) dB");
            return match.Success ? double.Parse(match.Groups["volume"].Value) : 0.0;
        }

        private async Task SendMessageToSQSAsync(Stream stream, double volume, string status)
        { 
            _logger.LogInformation("Posting message to SQS for {url}", stream.Url);
            var result = new
            {
                stream_id = stream.Id,
                account_id = stream.AccountId,
                volume,
                status,
                timestamp = DateTime.UtcNow
            };

            var sendMessageRequest = new SendMessageRequest
            {
                QueueUrl = _configuration["AppSettings:SQSQueueUrl"],
                MessageBody = JsonConvert.SerializeObject(result)
            };

            await _sqsClient.SendMessageAsync(sendMessageRequest);
            _logger.LogInformation("Posting message to SQS for successful {url}", stream.Url);
        }
    }
}
