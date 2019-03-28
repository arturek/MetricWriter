using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using Microsoft.ApplicationInsights;

namespace MetricWriter
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length != 1)
            {
                Console.WriteLine("InstrumentationKey missing");
                return;
            }

            var telemetryClient = new TelemetryClient(new Microsoft.ApplicationInsights.Extensibility.TelemetryConfiguration
            {
                InstrumentationKey = args[0],
            });


            //var metric = telemetryClient.GetMetric("art", "dim1");

            //for (int i = 0; i < 5; i++)
            //{
            //    metric.TrackValue(i, "aa");
            //    Thread.Sleep(TimeSpan.FromMinutes(1));
            //}


            var metricWriter = telemetryClient.GetMetricWriter("art", new MyMetricDimentions
            {
                Dim1 = "aa",
            });
            for (int i = 0; i < 5; i++)
            {
                metricWriter.TrackValue(i);

                Thread.Sleep(TimeSpan.FromMinutes(1));
            }
        }
    }

    public class MyMetricDimentions
    {
        public string Dim1 { get; set; }
    }

    public static class MetricExtensions
    {
        public static MetricWriter GetMetricWriter(this TelemetryClient telemetryClient, string metricId)
        {
            var metric = telemetryClient.GetMetric(metricId);
            return new MetricWriter(metric);
        }

        public static MetricWriter<T> GetMetricWriter<T>(this TelemetryClient telemetryClient, string metricId, T dimensions)
        {
            var properties = typeof(T).GetProperties();
            if (properties.Any(p => p.PropertyType != typeof(string)))
            {
                throw new Exception("Only value or dimension properties are allowee.");
            }

            var getMetricCall = typeof(TelemetryClient).GetMethod("GetMetric",
                Enumerable.Repeat(typeof(string), properties.Length + 1).ToArray());

            var getMetricCallParameters = new List<object>
            {
                metricId,
            };
            getMetricCallParameters.AddRange(properties.Select(x => x.Name));

            var metric = (Metric)getMetricCall.Invoke(telemetryClient, getMetricCallParameters.ToArray());

            return new MetricWriter<T>(BuildTrackValue<T>(properties), metric);
        }

        private static Func<Metric, double, T, bool> BuildTrackValue<T>(PropertyInfo[] dimensions)
        {
            var metricParameter = Expression.Parameter(typeof(Metric), "metric");
            var dimensionsParameter = Expression.Parameter(typeof(T), "dimensions");
            var valueParameter = Expression.Parameter(typeof(double), "trackedValue");

            var trackValueParameterTypes = new List<Type> { typeof(double) };
            trackValueParameterTypes.AddRange(Enumerable.Repeat(typeof(string), dimensions.Length));

            var trackValueParameters = new List<Expression> { valueParameter };
            trackValueParameters.AddRange(
                dimensions.Select(p => Expression.Property(dimensionsParameter, p)));
            var trackValueCall = Expression.Call(
                metricParameter,
                typeof(Metric).GetMethod("TrackValue", trackValueParameterTypes.ToArray()),
                trackValueParameters.ToArray());
            var del = Expression.Lambda(trackValueCall, metricParameter, valueParameter, dimensionsParameter);
            return (Func<Metric, double, T, bool>)del.Compile();
        }
    }

    public class MetricWriter<T>
    {
        private readonly Func<Metric, double, T, bool> _metricWriter;
        private readonly Metric _metric;

        public MetricWriter(Func<Metric, double, T, bool> metricWriter, Metric metric)
        {
            _metric = metric;
            _metricWriter = metricWriter;
        }

        public T DefaultDimensions { get; set; }

        public bool TrackValue(double value)
        {
            return _metricWriter(_metric, value, DefaultDimensions);
        }

        public bool TrackValue(int value, T dimensions)
        {
            return _metricWriter(_metric, value, dimensions);
        }
    }

    public class MetricWriter
    {
        private readonly Metric _metric;

        public MetricWriter(Metric metric)
        {
            _metric = metric;
        }

        public void TrackValue(double value)
        {
            _metric.TrackValue(value);
        }
    }
}
