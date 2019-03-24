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
            if(args.Length != 1)
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


            var metricWriter = telemetryClient.GetMetricWriter<MyMetric>("art");

            for (int i = 0; i < 5; i++)
            {
                metricWriter.Track(new MyMetric
                {
                    Value = i,
                    Dim1 = "aa",
                });

                Thread.Sleep(TimeSpan.FromMinutes(1));
            }
        }
    }

    public class MyMetric
    {
        public double Value { get; set; }

        public string Dim1 { get; set; }
    }

    public static class MetricExtensions
    {
        public static MetricWriter<T> GetMetricWriter<T>(this TelemetryClient telemetryClient, string metricId)
        {
            // TODO: validate parameters

            var properties = typeof(T).GetProperties();
            // TODO: take care of special properties
            PropertyInfo valueProperty = null;
            List<PropertyInfo> dimensionProperties = new List<PropertyInfo>();
            foreach (var property in properties)
            {
                if (property.PropertyType == typeof(double))
                {
                    if (valueProperty != null)
                        throw new Exception("Multiple value properties found.");
                    valueProperty = property;
                }
                else if (property.PropertyType == typeof(string))
                {
                    dimensionProperties.Add(property);
                }
                else
                {
                    throw new Exception("Only value or dimension properties are allowee.");
                }
            }

            if (valueProperty == null)
                throw new Exception("Value property is missing.");
            if (dimensionProperties.Count > 4)
                throw new Exception("Too many dimention specified.");

            // TODO: use MetricIdentifier to support more dimensions
            var getMetricCall = typeof(TelemetryClient).GetMethod("GetMetric",
                Enumerable.Repeat(typeof(string), dimensionProperties.Count + 1).ToArray());

            var getMetricCallParameters = new List<object>
            {
                metricId,
            };
            getMetricCallParameters.AddRange(dimensionProperties.Select(x => x.Name));

            var trackValueCall = BuildTrackValue<T>(valueProperty, dimensionProperties);
            var metric = (Metric)getMetricCall.Invoke(telemetryClient, getMetricCallParameters.ToArray());

            return new MetricWriter<T>(trackValueCall, metric);
        }

        private static Func<Metric, T, bool> BuildTrackValue<T>(PropertyInfo value, List<PropertyInfo> dimensions)
        {
            var metricParameter = Expression.Parameter(typeof(Metric), "metric");
            var valueParameter = Expression.Parameter(typeof(T), "trackedValue");
            var getValue = Expression.Property(valueParameter, value);
            var trackValueParameterTypes = new List<Type> { typeof(double) };
            trackValueParameterTypes.AddRange(Enumerable.Repeat(typeof(string), dimensions.Count));
            var trackValueParameters = new List<Expression> { getValue };
            trackValueParameters.AddRange(
                dimensions.Select(p => Expression.Property(valueParameter, p)));
            var trackValueCall = Expression.Call(
                metricParameter,
                typeof(Metric).GetMethod("TrackValue", trackValueParameterTypes.ToArray()),
                trackValueParameters.ToArray());
            var del = Expression.Lambda(trackValueCall, metricParameter, valueParameter);
            return (Func<Metric, T, bool>)del.Compile();
        }
    }

    public class MetricWriter<T>
    {
        private readonly Func<Metric, T, bool> _trackValue;
        private readonly Metric _metric;

        internal MetricWriter(Func<Metric, T, bool> trackValue, Metric metric)
        {
            _trackValue = trackValue;
            _metric = metric;
        }

        public bool Track(T value)
        {
            return _trackValue(_metric, value);
        }
    }
}
