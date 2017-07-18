using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Amazon.ElasticBeanstalk;
using Amazon.ElasticBeanstalk.Model;
using Amazon.Lambda.Core;

using Newtonsoft.Json;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

namespace BeanstalkToSlack
{
    public class Function
    {
        public async Task FunctionHandler(EventData input, ILambdaContext context)
        {
            await SendToSlack("#random", input.Records[0].Sns.Message);
        }

        private static async Task SendToSlack(string channel, MessageData data)
        {
            var beanstalkClient = new AmazonElasticBeanstalkClient();

            var response = await beanstalkClient.DescribeEnvironmentsAsync(new DescribeEnvironmentsRequest
            {
                ApplicationName = data.Application,
                EnvironmentNames = new List<string> { data.Environment }
            });

            var httpClient = new HttpClient();

            var payload = new
            {
                channel = channel,
                username = "Elastic Beanstalk",
                attachments = new[]
                {
                    new
                    {
                        color = data.Status.ToString().ToLower(),
                        text = data.Message,
                        fields = new object[]
                        {
                            new
                            {
                                title = "Version Label",
                                value = response.Environments[0].VersionLabel
                            },
                            new
                            {
                                title = "Application",
                                value = $"<https://ap-northeast-1.console.aws.amazon.com/elasticbeanstalk/home?region=ap-northeast-1#/application/overview?applicationName={data.Application}|{data.Application}>",
                                @short = true
                            },
                            new
                            {
                                title = "Environment",
                                value = $"<https://ap-northeast-1.console.aws.amazon.com/elasticbeanstalk/home?region=ap-northeast-1#/environment/dashboard?applicationName={data.Application}&environmentId={response.Environments[0].EnvironmentId}|{data.Environment}>",
                                @short = true
                            },
                            new
                            {
                                title = "Environment URL",
                                value = data.EnvironmentUrl
                            },
                            new
                            {
                                title = "Timestamp",
                                value = data.Timestamp
                            }
                        }
                    }
                }
            };

            var jsonString = JsonConvert.SerializeObject(payload);

            await httpClient.PostAsync("https://hooks.slack.com/services/XXXXX", new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("payload", jsonString)
            }));
        }
    }

    public enum StatusType
    {
        Info,
        Good,
        Warning,
        Danger
    }

    public static class MessageDataExtension
    {
        public static StatusType GetStatus(string message)
        {
            if (DangerMessages.Any(message.Contains))
            {
                return StatusType.Danger;
            }

            if (WarningMessages.Any(message.Contains))
            {
                return StatusType.Warning;
            }

            if (InfoMessage.Any(message.Contains))
            {
                return StatusType.Info;
            }

            return StatusType.Good;
        }

        private static readonly string[] DangerMessages =
        {
            " but with errors",
            " to RED",
            " to Degraded",
            " to Severe",
            "During an aborted deployment",
            "Failed to deploy",
            "Failed to deploy",
            "has a dependent object",
            "is not authorized to perform",
            "Pending to Degraded",
            "Stack deletion failed",
            "Unsuccessful command execution",
            "You do not have permission",
            "Your quota allows for 0 more running instance"
        };

        private static readonly string[] WarningMessages =
        {
            " to YELLOW",
            " to Warning",
            " aborted operation",
            "Degraded to Info",
            "Deleting SNS topic",
            "is currently running under desired capacity",
            "Ok to Info",
            "Ok to Warning",
            "Pending Initialization",
            "Rollback of environment"
        };

        private static readonly string[] InfoMessage =
        {
            "Adding instance",
            "Removed instance"
        };
    }

    public class MessageData
    {
        public StatusType Status => MessageDataExtension.GetStatus(Message);
        public DateTime Timestamp { get; set; }
        public string Message { get; set; }
        public string Environment { get; set; }
        public string Application { get; set; }
        public string EnvironmentUrl { get; set; }
    }

    public class EventData
    {
        public Record[] Records { get; set; }
    }

    public class Record
    {
        public Sns Sns { get; set; }
    }

    public class Sns
    {
        public string Subject { get; set; }

        [JsonConverter(typeof(MessageDataConverter))]
        public MessageData Message { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class MessageDataConverter : JsonConverter
    {
        private static readonly Regex _regex = new Regex(@"Timestamp:\s(?<timestamp>.*?)\n.*?Message:\s(?<message>.*?)\n.*?Environment:\s(?<environment>.*?)\n.*?Application:\s(?<application>.*?)\n.*?Environment URL:\s(?<environmentUrl>.*?)\n.*?", RegexOptions.Singleline);

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var value = (string)reader.Value;

            var match = _regex.Match(value);

            if (!match.Success)
            {
                return null;
            }

            return new MessageData
            {
                Timestamp = DateTime.ParseExact(match.Groups["timestamp"].Value, "ddd MMM dd HH':'mm':'ss UTC yyyy", DateTimeFormatInfo.InvariantInfo, DateTimeStyles.AssumeUniversal),
                Message = match.Groups["message"].Value,
                Environment = match.Groups["environment"].Value,
                Application = match.Groups["application"].Value,
                EnvironmentUrl = match.Groups["environmentUrl"].Value
            };
        }

        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(string);
        }
    }
}
