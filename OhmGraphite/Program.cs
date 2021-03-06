﻿using NLog;
using LibreHardwareMonitor.Hardware;
using Prometheus;
using Topshelf;

namespace OhmGraphite
{
    internal class Program
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private static void Main()
        {
            HostFactory.Run(x =>
            {
                x.Service<IManage>(s =>
                {
                    // We'll want to capture all available hardware metrics
                    // to send to graphite
                    var computer = new Computer
                    {
                        IsGpuEnabled = true,
                        IsMotherboardEnabled = true,
                        IsCpuEnabled = true,
                        IsMemoryEnabled = true,
                        IsNetworkEnabled = true,
                        IsStorageEnabled = true,
                        IsControllerEnabled = true
                    };
                    var collector = new SensorCollector(computer);

                    // We need to know where the graphite server lives and how often
                    // to poll the hardware
                    var config = Logger.LogFunction("parse config", () => MetricConfig.ParseAppSettings(new AppConfigManager()));
                    var metricsManager = CreateManager(config, collector);

                    s.ConstructUsing(name => metricsManager);
                    s.WhenStarted(tc => tc.Start());
                    s.WhenStopped(tc => tc.Dispose());
                });
                x.UseNLog();
                x.RunAsLocalSystem();
                x.SetDescription(
                    "Extract hardware sensor data and exports it to a given host and port in a graphite compatible format");
                x.SetDisplayName("Ohm Graphite");
                x.SetServiceName("OhmGraphite");
                x.OnException(ex => Logger.Error(ex, "OhmGraphite TopShelf encountered an error"));
            });
        }

        private static IManage CreateManager(MetricConfig config, SensorCollector collector)
        {
            var hostname = config.LookupName();
            double seconds = config.Interval.TotalSeconds;
            if (config.Graphite != null)
            {
                Logger.Info(
                    $"Graphite host: {config.Graphite.Host} port: {config.Graphite.Port} interval: {seconds} tags: {config.Graphite.Tags}");
                var writer = new GraphiteWriter(config.Graphite.Host,
                    config.Graphite.Port,
                    hostname,
                    config.Graphite.Tags);
                return new MetricTimer(config.Interval, collector, writer);
            }
            else if (config.Prometheus != null)
            {
                Logger.Info($"Prometheus port: {config.Prometheus.Port}");
                var registry = PrometheusCollection.SetupDefault(collector);
                var server = new MetricServer(config.Prometheus.Host, config.Prometheus.Port, registry: registry);
                return new PrometheusServer(server, collector);
            }
            else if (config.Timescale != null)
            {
                var writer = new TimescaleWriter(config.Timescale.Connection, config.Timescale.SetupTable, hostname);
                return new MetricTimer(config.Interval, collector, writer);
            }
            else
            {
                Logger.Info($"Influxdb address: {config.Influx.Address} db: {config.Influx.Db}");
                var writer = new InfluxWriter(config.Influx, hostname);
                return new MetricTimer(config.Interval, collector, writer);
            }
        }
    }
}