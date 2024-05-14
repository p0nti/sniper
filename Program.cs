using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Sniper.DTO.PubToken;
using Sniper.DTO.Ticker;
using Sniper.Services;
using Microsoft.Extensions.DependencyInjection;
// using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
// using Serilog.Extensions.Logging;
// using Serilog.Sinks.File;
// using Serilog.Sinks.SystemConsole;
using Serilog;

namespace Sniper
{
    internal class AppService
    {
        private readonly ILogger _logger;
        private readonly IPriceService _priceService;

        public AppService(ILogger logger, IPriceService priceService)
        {
            _logger = logger;
            _priceService = priceService;
        }

        internal async Task Run()
        {
        label1:

            try
            {

                using (var wc = new WebClient())
                {
                    var result = wc.UploadData("https://api.kucoin.com/api/v1/bullet-public", new byte[] { });
                    var json = Encoding.ASCII.GetString(result);

                    var initialResponse = JsonConvert.DeserializeObject<PubTokenDTO>(json);
                    var connectId = Guid.NewGuid();
                    var url = $"{initialResponse.data.instanceServers[0].endpoint}?token={initialResponse.data.token}&[connectId={connectId}]";
                    _logger.Information(url);

                    using (var client = new ClientWebSocket())
                    {
                        //connect to api
                        var source = new CancellationTokenSource();
                        var token = source.Token;
                        await client.ConnectAsync(new Uri(url), token);

                        // get welcome message
                        var welcomeMsg = new ArraySegment<byte>(new byte[2500]);
                        await client.ReceiveAsync(welcomeMsg, token);
                        json = Encoding.ASCII.GetString(welcomeMsg.Array);
                        _logger.Information(json);

                        // subscribe to topic
                        var subscribeMsg =
                            $"{{\"id\": {new Random(50000).Next()},\"type\": \"subscribe\",\"topic\": \"/market/ticker:all\",\"response\": true}}";
                        _logger.Information(subscribeMsg);
                        var subscriptionMsg = new ArraySegment<byte>(Encoding.ASCII.GetBytes(subscribeMsg));
                        await client.SendAsync(subscriptionMsg, WebSocketMessageType.Text, false, token);

                        var subscriptionResultMsg = new ArraySegment<byte>(new byte[40000]);
                        await client.ReceiveAsync(subscriptionResultMsg, token);
                        json = Encoding.ASCII.GetString(subscriptionResultMsg.Array);
                        _logger.Debug(json);

                        //  PriceService priceService = new PriceService(5);
                        while (!client.CloseStatus.HasValue)
                        {
                            // Thread.Sleep(100);
                            var tickerMsg = new ArraySegment<byte>(new byte[2000]);
                            await client.ReceiveAsync(tickerMsg, token);
                            json = Encoding.ASCII.GetString(tickerMsg.Array);
                            var tickerDTO = JsonConvert.DeserializeObject<TickerDTO>(json);

                            // print
                            _priceService.Add(tickerDTO);
                            _priceService.PrintPriceDiff(PriceDiffRange.Increase);


                            // if (json.IndexOf("SWP-USDT") != -1)
                            //     Console.WriteLine(json);
                        }

                        await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "", token);
                    }
                }
            }
            catch (Exception e)
            {
                _logger.Error(e.Message);
                goto label1;
            }
        }
    }

    internal class Program
    {
        private static void ConfigureServices(ServiceCollection services)
        {

            Serilog.Debugging.SelfLog.Enable(msg => Console.WriteLine(msg));
            Serilog.Debugging.SelfLog.Enable(Console.Error);

            var configuration = new ConfigurationBuilder()
            // .SetBasePath(Directory.GetParent(AppContext.BaseDirectory).FullName)
            .AddJsonFile("appsettings.json", false, true)
            .Build();


            services
                .AddTransient<AppService>()
                .AddTransient<IPriceService, PriceService>()
                .AddSingleton<IConfigurationRoot>(configuration);


            //setup logging

            // var serilogLogger = new LoggerConfiguration()
            //     .ReadFrom.Configuration(configuration)
            //     // .WriteTo.File("Output.log")
            //     // .WriteTo.Console()
            //     .CreateLogger();

            var seriLogger = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration)
                .CreateLogger();
            // services.AddLogging(builder =>
            //     {
            //         builder.AddSerilog(seriLogger, true);
            //     });

            services.AddSingleton<ILogger>(seriLogger);
        }

        public static async Task Main(string[] args)
        {
            var services = new ServiceCollection();
            ConfigureServices(services);
            using (ServiceProvider serviceProvider = services.BuildServiceProvider())
            {
                var appService = serviceProvider.GetService<AppService>();
                if (appService == null)
                    throw new NullReferenceException("appservice is null");
                await appService.Run();
            }
        }
    }
}