using System.Globalization;
using System.Security;
using System.Security.Cryptography;
using System.Text;

namespace CamGuard.Api.Features.Onvif;

/// <summary>Credenciales WS-Security de una cámara ONVIF.</summary>
public record OnvifCredentials(string Username, string Password);

/// <summary>
/// Arma los sobres SOAP 1.2 de ONVIF: WS-Addressing (Action y To) y WS-Security UsernameToken con
/// PasswordDigest. Sin dependencias para poder probarla: el nonce y la hora llegan por parámetro.
/// </summary>
public static class OnvifSoap
{
    public const string DeviceNs = "http://www.onvif.org/ver10/device/wsdl";
    public const string MediaNs = "http://www.onvif.org/ver10/media/wsdl";
    public const string EventsNs = "http://www.onvif.org/ver10/events/wsdl";
    public const string SchemaNs = "http://www.onvif.org/ver10/schema";
    public const string WsnNs = "http://docs.oasis-open.org/wsn/b-2";

    public static class Actions
    {
        public const string GetSystemDateAndTime = DeviceNs + "/GetSystemDateAndTime";
        public const string GetCapabilities = DeviceNs + "/GetCapabilities";
        public const string GetDeviceInformation = DeviceNs + "/GetDeviceInformation";
        public const string GetProfiles = MediaNs + "/GetProfiles";
        public const string GetStreamUri = MediaNs + "/GetStreamUri";
        public const string CreatePullPointSubscription = EventsNs + "/EventPortType/CreatePullPointSubscriptionRequest";
        public const string PullMessages = EventsNs + "/PullPointSubscription/PullMessagesRequest";
        public const string Renew = "http://docs.oasis-open.org/wsn/bw-2/SubscriptionManager/RenewRequest";
        public const string Unsubscribe = "http://docs.oasis-open.org/wsn/bw-2/SubscriptionManager/UnsubscribeRequest";
    }

    public static class Bodies
    {
        public const string GetSystemDateAndTime = $"<GetSystemDateAndTime xmlns=\"{DeviceNs}\"/>";
        public const string GetCapabilities = $"<GetCapabilities xmlns=\"{DeviceNs}\"><Category>All</Category></GetCapabilities>";
        public const string GetDeviceInformation = $"<GetDeviceInformation xmlns=\"{DeviceNs}\"/>";
        public const string GetProfiles = $"<GetProfiles xmlns=\"{MediaNs}\"/>";
        public const string Unsubscribe = $"<Unsubscribe xmlns=\"{WsnNs}\"/>";

        public static string GetStreamUri(string profileToken) =>
            $"<GetStreamUri xmlns=\"{MediaNs}\"><StreamSetup>" +
            $"<Stream xmlns=\"{SchemaNs}\">RTP-Unicast</Stream>" +
            $"<Transport xmlns=\"{SchemaNs}\"><Protocol>RTSP</Protocol></Transport>" +
            $"</StreamSetup><ProfileToken>{SecurityElement.Escape(profileToken)}</ProfileToken></GetStreamUri>";

        public static string CreatePullPointSubscription(TimeSpan termination) =>
            $"<CreatePullPointSubscription xmlns=\"{EventsNs}\"><InitialTerminationTime>{Duration(termination)}</InitialTerminationTime></CreatePullPointSubscription>";

        public static string PullMessages(TimeSpan timeout, int limit) =>
            $"<PullMessages xmlns=\"{EventsNs}\"><Timeout>{Duration(timeout)}</Timeout><MessageLimit>{limit.ToString(CultureInfo.InvariantCulture)}</MessageLimit></PullMessages>";

        public static string Renew(TimeSpan termination) =>
            $"<Renew xmlns=\"{WsnNs}\"><TerminationTime>{Duration(termination)}</TerminationTime></Renew>";
    }

    /// <summary>Duración xs:duration en segundos enteros (p. ej. PT10S).</summary>
    public static string Duration(TimeSpan span) =>
        $"PT{Math.Max(1, (int)Math.Ceiling(span.TotalSeconds)).ToString(CultureInfo.InvariantCulture)}S";

    /// <summary>Hora del UsernameToken en UTC con milisegundos, como la esperan las cámaras.</summary>
    public static string FormatCreated(DateTimeOffset at) =>
        at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    /// <summary>PasswordDigest = Base64(SHA1(nonce + created + password)).</summary>
    public static string PasswordDigest(byte[] nonce, string created, string password)
    {
        var text = Encoding.UTF8.GetBytes(created + password);
        var buffer = new byte[nonce.Length + text.Length];
        nonce.CopyTo(buffer, 0);
        text.CopyTo(buffer, nonce.Length);
        return Convert.ToBase64String(SHA1.HashData(buffer));
    }

    /// <param name="credentials">Null = sin autenticación (p. ej. GetSystemDateAndTime).</param>
    /// <param name="created">Hora de la cámara (la local más el desfase medido), no la del equipo.</param>
    public static string Envelope(string action, string to, string body, OnvifCredentials? credentials, DateTimeOffset created, byte[] nonce)
    {
        var security = "";
        if (credentials is not null)
        {
            var createdText = FormatCreated(created);
            security =
                "<wsse:Security s:mustUnderstand=\"1\" " +
                "xmlns:wsse=\"http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd\" " +
                "xmlns:wsu=\"http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd\">" +
                "<wsse:UsernameToken>" +
                $"<wsse:Username>{SecurityElement.Escape(credentials.Username)}</wsse:Username>" +
                "<wsse:Password Type=\"http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-username-token-profile-1.0#PasswordDigest\">" +
                $"{PasswordDigest(nonce, createdText, credentials.Password)}</wsse:Password>" +
                "<wsse:Nonce EncodingType=\"http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-soap-message-security-1.0#Base64Binary\">" +
                $"{Convert.ToBase64String(nonce)}</wsse:Nonce>" +
                $"<wsu:Created>{createdText}</wsu:Created>" +
                "</wsse:UsernameToken></wsse:Security>";
        }

        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
               "<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\" xmlns:wsa=\"http://www.w3.org/2005/08/addressing\">" +
               $"<s:Header>{security}<wsa:Action>{SecurityElement.Escape(action)}</wsa:Action><wsa:To>{SecurityElement.Escape(to)}</wsa:To></s:Header>" +
               $"<s:Body>{body}</s:Body></s:Envelope>";
    }
}
