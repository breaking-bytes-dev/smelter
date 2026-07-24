using System;

namespace Smelter.Plugins.Services
{
    /// <summary>
    /// Thrown by <see cref="TemplateSerializer"/> when a template JSON payload is empty, malformed,
    /// has an unsupported format version, or fails structural validation (orphan refs).
    /// <para>
    /// Lives in the pure core (no Dataverse SDK dependency); the plugin layer translates it to
    /// <c>InvalidPluginExecutionException</c> so the error surfaces cleanly in Power Automate.
    /// </para>
    /// </summary>
    public class TemplateFormatException : Exception
    {
        public TemplateFormatException(string message) : base(message) { }
    }
}
