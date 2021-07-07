using System;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Volume.QBox.RasaApi.Interfaces;
using Volume.QBox.RasaApi.Models;

namespace Volume.QBox.RasaApi
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers();

            services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.KnownProxies.Add(IPAddress.Parse(Configuration.GetValue<string>("AppSettings:ForwardIPAddress")));
            });

            services.Add(new ServiceDescriptor(typeof(IProcessService), typeof(Services.ProcessService), ServiceLifetime.Singleton));
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ILogger<Startup> logger, ILoggerFactory loggerFactory, IProcessService processService)
        {
            int logLevel = Configuration.GetValue<int>("AppSettings:LogLevel");
            string LogDir = Configuration.GetValue<string>("AppSettings:LogDir");
            Console.WriteLine("LogDir: " + LogDir);
            loggerFactory.AddFile(LogDir + "/RasaAPI-{Date}.log", (LogLevel)logLevel, null, false, 5000000, 14, "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level}] {Message}{NewLine}{Exception}");

            var rasaVersions = Configuration.GetSection("Rasa").Get<RasaVersionModel[]>();
            processService.SetRasaVersions(rasaVersions);

            app.UseExceptionHandler(a => a.Run(async context =>
            {
                var exceptionHandlerPathFeature = context.Features.Get<IExceptionHandlerPathFeature>();
                var exception = exceptionHandlerPathFeature.Error;
                logger.LogError($"Unhandled exception: {exception.Message}");
                var result = JsonConvert.SerializeObject(new { error = exception.Message });
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(result);
            }));

            app.UseHttpsRedirection();
            app.UseRouting();

            app.UseForwardedHeaders(new ForwardedHeadersOptions()
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
            });

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }
}