using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Rts.Providers
{
    /// <summary>Common judgement port for Jev, a local OpenAI-compatible model, and fakes.</summary>
    public interface IJudgement : IJevTransport
    {
    }

    public enum JudgementBackend
    {
        Jev,
        LocalLlm
    }

    /// <summary>Runtime-selectable backend settings. The default is the official Jev endpoint.</summary>
    public sealed class JudgementSettings
    {
        public JudgementBackend Backend = JudgementBackend.Jev;
        public string Url = HttpJevTransport.DefaultUrl;
        public string Model = HttpJevTransport.DefaultModel;
        public string KeyEnvironment = HttpJevTransport.DefaultKeyEnvironment;
        public TimeSpan Timeout = TimeSpan.FromSeconds(12);
        public long IntervalTicks = 200;
    }

    public static class JudgementTransportFactory
    {
        public static IJevTransport Create(JudgementSettings settings, Func<string> readKey = null,
            HttpMessageHandler handler = null)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (settings.Backend == JudgementBackend.LocalLlm)
                return new LocalLlmTransport(settings.Url ?? LocalLlmTransport.DefaultUrl,
                    settings.Model ?? LocalLlmTransport.DefaultModel, handler, settings.Timeout);
            if (readKey == null) throw new ArgumentNullException(nameof(readKey));
            return new HttpJevTransport(readKey, handler, settings.Url ?? HttpJevTransport.DefaultUrl,
                settings.Model ?? HttpJevTransport.DefaultModel, settings.Timeout);
        }
    }
}
