using System;

namespace PortForwardServer
{
    public enum MessageType
    {
        HEARTBEAT,
        REQUEST_CHECK,
        REPLY_CHECK,
        DISCONNECT,
    }
}

