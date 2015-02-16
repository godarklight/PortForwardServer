using System;
using System.Net;
using System.Net.Sockets;

namespace PortForwardServer
{
    public class ClientObject
    {
        public long lastReceive;
        public long lastSend;
        public IPAddress remoteAddress;
        public TcpClient tcpClient;
        public MessageType receiveType;
        public byte[] receiveBuffer;
        public int receiveLeft;
        public bool receivingPayload;
    }
}

