using System.Net;
using System.Text;
using System.Web;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Discord.Rest;

using Discord.Doorway.Lib;
using static Discord.Doorway.Lib.helpers;

namespace Discord.Doorway
{
    public class Function
    {
        private readonly ILogger _logger;

        public Function(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<Function>();
        }

        [Function("Doorway")]
        public MultiOutputBinding Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequestData req)
        {
            _logger.LogInformation("C# HTTP trigger function received a request.");

            var httpresponse = req.CreateResponse(HttpStatusCode.OK);
            httpresponse.Headers.Add("Content-Type", "application/json; charset=utf-8");
            var queryParams = HttpUtility.ParseQueryString(req.Url.Query);
            StreamReader reader = new StreamReader(req.Body);
            string text = reader.ReadToEnd();
            _logger.LogInformation("Request: " + text);
            response r = new response();

            // #region parse command body
            discordInteraction d = new discordInteraction();
            try
            {
                discordInteraction? parsed = JsonSerializer.Deserialize<discordInteraction>(text);
                if (null == parsed)
                {
                    _logger.LogInformation("No input data");
                    return new MultiOutputBinding()
                    {
                        HttpReponse = req.CreateResponse(HttpStatusCode.BadRequest)
                    };
                }
                d.application_id = parsed.application_id;
                d.type = parsed.type;
                d.data = parsed.data;
            }
            catch
            {
                _logger.LogInformation("Couldn't parse via json deserializer");

                try
                {
                    System.Collections.Specialized.NameValueCollection? parsed = HttpUtility.ParseQueryString(text);
                    _logger.LogInformation("Keys detected: " + String.Join(" ", parsed.AllKeys));
                    d.application_id = parsed["application_id"];
                }
                catch
                {
                    _logger.LogInformation("Couldn't parse via x-www-urlencoding deserializer either");
                    httpresponse.StatusCode = HttpStatusCode.BadRequest;
                    // response.WriteString("Bad input, application_id must be present and a string");
                    return new MultiOutputBinding()
                    {
                        HttpReponse = httpresponse
                    };
                }
            }
            // #endregion




            // #region Signature verification
            if (req.Url.Host == "localhost" && req.Url.Port == 7071)
            {
                _logger.LogWarning("Skipping signature verification");
            }
            else
            {
                // Must reply with 401 unauthorized if no ed25519 signature or timestamp, or if bad validation
                // https://discord.com/developers/docs/interactions/receiving-and-responding#security-and-authorization

                string pubKey = GetEnvironmentVariable("APP_PUBLICKEY_" + d.application_id);
                if (string.IsNullOrWhiteSpace(pubKey))
                {
                    _logger.LogInformation("No pubkey");
                    httpresponse.StatusCode = HttpStatusCode.BadRequest;
                    return new MultiOutputBinding()
                    {
                        HttpReponse = httpresponse
                    };
                }

                var headers = req.Headers;
                if (!headers.TryGetValues("X-Signature-Timestamp", out var timestamp) ||
                    !headers.TryGetValues("X-Signature-Ed25519", out var signature))
                {
                    _logger.LogInformation("No timestamp or no sig");
                    return new MultiOutputBinding()
                    {
                        HttpReponse = req.CreateResponse(HttpStatusCode.Unauthorized)
                    };
                }

                _logger.LogInformation("Sig: " + signature.First());
                _logger.LogInformation("ts: " + timestamp.First());

                DiscordRestClient DiscordRestClient = new DiscordRestClient();
                bool validated = false;
                validated = DiscordRestClient.IsValidHttpInteraction(pubKey, signature.First(), timestamp.First(), text);

                if (validated == false)
                {
                    _logger.LogInformation("Not valid signature");
                    return new MultiOutputBinding()
                    {
                        HttpReponse = req.CreateResponse(HttpStatusCode.Unauthorized)
                    };
                }
            }
            // #endregion

            // Respond to pings with ACK
            if (d.type == 1)
            {
                _logger.LogInformation("Type 1 found!");
                r.type = 1;
                httpresponse.WriteString(JsonSerializer.Serialize(r));
                return new MultiOutputBinding()
                {
                    HttpReponse = httpresponse
                };
            }

            // #region Retrieve command name
            // Build a string from the application command and then go and get that ENV variable.
            // Parse it to a responseEmbed obj. Return!
            // Example equivilent: GetLink("LINK_GET_BATTALIONS");
            StringBuilder sb = new StringBuilder("APP_COMMAND_");
            if (!string.IsNullOrWhiteSpace(d.data.name))
            {
                sb.Append(d.data.name);
                if (
                    null != d.data.options &&
                    d.data.options.Any() &&
                    !string.IsNullOrWhiteSpace(d.data.options[0].name) &&
                    d.data.options[0].type <= 2
                )
                {
                    sb.Append("_" + d.data.options[0].name);
                    if (
                        null != d.data.options[0].options &&
                        d.data.options[0].options.Any() &&
                        !string.IsNullOrWhiteSpace(d.data.options[0].options[0].name) &&
                        d.data.options[0].options[0].type <= 2
                    )
                    {
                        sb.Append("_" + d.data.options[0].options[0].name);
                    }
                }
            }
            string command = sb.ToString().ToUpper();
            _logger.LogInformation("Command is: " + command);
            // #endregion

            // #region command canned response
            if (string.IsNullOrWhiteSpace(command))
            {
                r.type = 5;
                httpresponse.WriteString(JsonSerializer.Serialize(r));
                return new MultiOutputBinding()
                {
                    HttpReponse = httpresponse,
                    QueueBody = text
                };
            }
            _logger.LogInformation("checking for canned for command");
            string cannedResponse = "";
            cannedResponse = GetEnvironmentVariable(command);
            // Maybe enable application specific commands in the future:
            // Maybe enable guild specific commands in the future too!
            // if (string.IsNullOrWhiteSpace(cannedResponse))
            // {
            //     _logger.LogInformation("Check if there is an app specific canned response");
            //     cannedResponse = GetEnvironmentVariable(command + "_" + d.application_id);
            // }

            if (string.IsNullOrWhiteSpace(cannedResponse))
            {
                r.type = 5;
                httpresponse.WriteString(JsonSerializer.Serialize(r));
                return new MultiOutputBinding()
                {
                    HttpReponse = httpresponse,
                    QueueBody = text
                };
            }

            _logger.LogInformation("Found a canned response! " + cannedResponse);
            r.data = new responseData();
            r.type = 4;
            // standard canned command properties should get applied to the response as needed
            bool passthru = false;
            try
            {
                _logger.LogInformation("Trying to parse");
                CannedCommand? cc = JsonSerializer.Deserialize<CannedCommand>(cannedResponse);
                if (null == cc)
                {
                    Thread.Sleep(300);
                    _logger.LogInformation("Failed to get any data out");
                    throw new NullReferenceException();
                }
                if (cc.@private == true)
                {
                    _logger.LogInformation("Adding private flag");
                    r.data.flags = Lib.MessageFlags.EPHEMERAL;
                }
                if (cc.passthru == true)
                {
                    _logger.LogInformation("Doing passthru");
                    passthru = true;
                }
            }
            catch
            {
                _logger.LogInformation("no private or passthru, skipping");
            }


            // free form content of canned command needs handled nicely
            try
            {
                CannedCommandNested? ccn = JsonSerializer.Deserialize<CannedCommandNested>(cannedResponse);
                if (null == ccn || null == ccn.content)
                {
                    throw new NullReferenceException();
                }
                cannedResponse = JsonSerializer.Serialize(ccn.content);

            }
            catch
            {
                try
                {
                    CannedCommandSimple? ccs = JsonSerializer.Deserialize<CannedCommandSimple>(cannedResponse);
                    if (null == ccs || String.IsNullOrWhiteSpace(ccs.content))
                    {
                        throw new NullReferenceException();
                    }
                    cannedResponse = JsonSerializer.Serialize(ccs.content);
                }
                catch { }
            }


            // Need to handle various canned command possibilites
            try
            {
                responseEmbed? da = JsonSerializer.Deserialize<responseEmbed>(cannedResponse);
                if (null != da)
                {
                    List<responseEmbed> l = new List<responseEmbed>();
                    l.Add(da);
                    r.data.embeds = l.ToArray();
                    _logger.LogInformation("Setting to embed mode for response");
                }
                else
                {
                    _logger.LogWarning("Command embed was literally null");
                }
            }
            catch
            {
                _logger.LogInformation("Setting to content mode for response");
                r.data.content = cannedResponse;
            }
            httpresponse.WriteString(JsonSerializer.Serialize(r));

            if (r.type == 5 || passthru)
            {
                return new MultiOutputBinding()
                {
                    QueueBody = text,
                    HttpReponse = httpresponse
                };
            }
            else
            {
                return new MultiOutputBinding()
                {
                    HttpReponse = httpresponse
                };
            }
        }
    }
}
