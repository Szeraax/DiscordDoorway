using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using System;
using System.Linq;
using System.Collections.Generic;

namespace Discord.Doorway.Lib
{
    public class helpers
    {
        public static string CannedResponse(string variable)
        {
            string value = GetEnvironmentVariable(variable);
            if (null == value)
            {
                throw new KeyNotFoundException("Can't find that URI: " + variable);
            }
            else
            {
                return value;
            }
        }

        public static string GetEnvironmentVariable(string name)
        {
            string? @out = System.Environment.GetEnvironmentVariable(name);
            if (null == @out)
            {
                return "";
            }
            else
            {
                return @out;
            }
        }

        public static byte[] StringToByteArray(string hex)
        {
            return Enumerable.Range(0, hex.Length)
                             .Where(x => x % 2 == 0)
                             .Select(x => Convert.ToByte(hex.Substring(x, 2), 16))
                             .ToArray();
        }
    }

    public class MultiOutputBinding
    {
        [QueueOutput("{application_id}", Connection = "AzureWebJobsStorage")]
        public string? QueueBody { get; set; }
        public HttpResponseData HttpReponse { get; set; }
    }
    public class discordInteraction
    {
        public string application_id { get; set; }
        public int type { get; set; }
        public string guild_id { get; set; }
        public discordInteractionData data { get; set; } // Shouldn't need
    }

    public class discordInteractionData
    {
        public string name { get; set; }
        public int type { get; set; }
        public string target_id { get; set; }
        public discordInteractionDataOptions[] options { get; set; }
    }

    public class discordInteractionDataOptions
    {
        public string name { get; set; }
        public int type { get; set; }
        public string value { get; set; }
        public discordInteractionDataOptions[] options { get; set; }
    }

    public class response
    {
        public int type { get; set; }
        public responseData data { get; set; }
    }
    public class responseData
    {
        public string content { get; set; }
        public MessageFlags flags { get; set; }
        public responseEmbed[] embeds { get; set; }
    }

    public class responseEmbed
    {
        public string? title { get; set; }
        public string? url { get; set; }
        public string? description { get; set; }
        public int? color { get; set; }
    }

    public class CannedCommand
    {
        public bool? @private { get; set; }
        public bool? passthru { get; set; }
    }
    public class CannedCommandNested
    {
        public responseEmbed? content { get; set; }
    }
    public class CannedCommandSimple
    {
        public string? content { get; set; }
    }

    [System.Flags]
    public enum MessageFlags
    {
        NONE = 0 << 0,
        CROSSPOSTED = 1 << 0,
        IS_CROSSPOST = 1 << 1,
        SUPPRESS_EMBEDS = 1 << 2,
        SOURCE_MESSAGE_DELETED = 1 << 3,
        URGENT = 1 << 4,
        HAS_THREAD = 1 << 5,
        EPHEMERAL = 1 << 6,
        LOADING = 1 << 7,
        FAILED_TO_MENTION_SOME_ROLES_IN_THREAD = 1 << 8
    }
}
