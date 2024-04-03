namespace COSMP.Network
{
    internal class COSMPConstants
    {
        internal const int NETWORK_PING = 3000;
        internal const int NETWORK_TIMEOUT = 5000;

        internal const string DEFAULT_SERVER_HOST = "127.0.0.1";
        internal const int DEFAULT_SERVER_PORT = 5923;
        internal const string SERVER_KEY = "COSMP";

        internal const int USERNAME_MAX_LENGTH = 20;

    }

    internal enum PacketType : byte
    {
        Unknown,
        Login,
        Meta,
        Join,
        Leave,
        Ping,
        Position,
        Look,
        Action,
        CanvasAction
    }

    internal enum NetworkErrorCode
    {
        Unknown,
        Connect,
        Disconnect,
        Kick,
        Ban,
    }
}
