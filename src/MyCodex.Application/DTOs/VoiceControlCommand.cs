using System;
using System.Collections.Generic;
using System.Globalization;
using MyCodex.Domain.Entities;

namespace MyCodex.Application.DTOs
{
    public sealed class VoiceControlCommand
    {
        public bool? Enabled { get; set; }
        public bool? MicrophoneMuted { get; set; }
        public bool? Faster { get; set; }
        public int? VoiceNumber { get; set; }
        public VoiceOutputLevel? OutputLevel { get; set; }
        public bool HasSettings => Faster.HasValue || VoiceNumber.HasValue || OutputLevel.HasValue;
        public bool HasChanges => Enabled.HasValue || MicrophoneMuted.HasValue || HasSettings;

        public void ValidateConversation(string threadId)
        {
            if ((threadId ?? string.Empty).Length > 256 || (threadId ?? string.Empty).IndexOf('\0') >= 0)
                throw new ArgumentException("Invalid voice conversation ID.");
            if (HasChanges && string.IsNullOrWhiteSpace(threadId))
                throw new ArgumentException("A conversation ID is required when changing voice controls.");
        }

        public static VoiceControlCommand Parse(object arguments)
        {
            var values = arguments == null ? new Dictionary<string, object>() : arguments as IDictionary<string, object>;
            if (values == null) throw new ArgumentException("Voice controls require an object.");
            var command = new VoiceControlCommand();
            foreach (var pair in values)
            {
                switch (pair.Key)
                {
                    case "enabled": command.Enabled = Boolean(pair.Value); break;
                    case "microphone_muted": command.MicrophoneMuted = Boolean(pair.Value); break;
                    case "faster": command.Faster = Boolean(pair.Value); break;
                    case "voice_number":
                        command.VoiceNumber = Integer(pair.Value);
                        if (command.VoiceNumber < 1) throw new ArgumentException("Voice numbers start at 1.");
                        break;
                    case "output_percent":
                        var value = Integer(pair.Value);
                        if (!Enum.IsDefined(typeof(VoiceOutputLevel), value)) throw new ArgumentException("Unsupported voice output level.");
                        command.OutputLevel = (VoiceOutputLevel)value;
                        break;
                    default: throw new ArgumentException("Unknown voice control parameter: " + pair.Key);
                }
            }
            if (command.Enabled == false && command.MicrophoneMuted == false) throw new ArgumentException("Cannot unmute while disabling voice.");
            return command;
        }

        private static bool Boolean(object value)
        {
            if (!(value is bool)) throw new ArgumentException("Expected a boolean voice control value.");
            return (bool)value;
        }

        private static int Integer(object value)
        {
            if (!(value is int || value is long || value is double || value is decimal)) throw new ArgumentException("Expected an integer voice control value.");
            var number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            if (double.IsNaN(number) || number < int.MinValue || number > int.MaxValue || number != Math.Truncate(number)) throw new ArgumentException("Expected an integer voice control value.");
            return (int)number;
        }
    }
}
