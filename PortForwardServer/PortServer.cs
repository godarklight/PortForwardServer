using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Net;
using System.Net.Sockets;
using MessageStream2;

namespace PortForwardServer
{
    public class PortServer
    {
        public bool Running
        {
            get;
            private set;
        }

        private List<ClientObject> clients = new List<ClientObject>();
        private ServerSettings serverSettings;
        private TcpListener tcpListener;
        private const int CONNECTION_TIMEOUT = 5000;
        private const int CLIENT_HEARTBEAT = 5000;
        private const int CLIENT_TIMEOUT = 20000;
        private const int TICKS_TO_MS = 10000;
        private Thread timeoutThread;

        public PortServer(ServerSettings serverSettings)
        {
            this.serverSettings = serverSettings;
        }

        public void Run()
        {
            if (!Running)
            {
                Running = true;            
                tcpListener = new TcpListener(IPAddress.IPv6Any, serverSettings.Port);
                tcpListener.Server.NoDelay = true;
                if (Environment.OSVersion.Platform != PlatformID.MacOSX && Environment.OSVersion.Platform != PlatformID.Unix)
                {
                    tcpListener.Server.SetSocketOption(SocketOptionLevel.IPv6, (SocketOptionName)27, 0);
                }
                tcpListener.Start();
                tcpListener.BeginAcceptTcpClient(ConnectionCallback, tcpListener);
                timeoutThread = new Thread(new ThreadStart(CheckTimeouts));
                timeoutThread.IsBackground = true;
                timeoutThread.Start();
                Console.WriteLine("Listening on port " + serverSettings.Port);
            }
        }

        private void CheckTimeouts()
        {
            while (true)
            {
                foreach (ClientObject client in clients.ToArray())
                {
                    try
                    {
                        if ((DateTime.UtcNow.Ticks - client.lastReceive) > CLIENT_TIMEOUT * TICKS_TO_MS)
                        {
                            Console.WriteLine("Timeout: " + client.remoteAddress);
                            DisconnectClient(client);
                        }
                        else
                        {
                            if ((DateTime.UtcNow.Ticks - client.lastSend) > CLIENT_HEARTBEAT * TICKS_TO_MS)
                            {
                                client.lastSend = DateTime.UtcNow.Ticks;
                                byte[] sendBytes;
                                using (MessageWriter mw = new MessageWriter())
                                {
                                    mw.Write<int>((int)MessageType.HEARTBEAT);
                                    mw.Write<int>(0);
                                    sendBytes = mw.GetMessageBytes();
                                }
                                client.tcpClient.GetStream().BeginWrite(sendBytes, 0, sendBytes.Length, null, null);
                            }
                        }
                    }
                    catch
                    {
                        DisconnectClient(client);
                    }
                }
                Thread.Sleep(100);
            }
        }

        private void ConnectionCallback(IAsyncResult ar)
        {
            TcpListener listenerObject = (TcpListener)ar.AsyncState;
            try
            {
                TcpClient newClient = listenerObject.EndAcceptTcpClient(ar);
                ClientObject newClientObject = new ClientObject();
                newClientObject.remoteAddress = ((IPEndPoint)newClient.Client.RemoteEndPoint).Address;
                newClientObject.tcpClient = newClient;
                newClientObject.receiveBuffer = new byte[8];
                newClientObject.receiveLeft = 8;
                newClientObject.lastReceive = DateTime.UtcNow.Ticks;
                newClientObject.lastSend = DateTime.UtcNow.Ticks;
                newClient.GetStream().BeginRead(newClientObject.receiveBuffer, 0, newClientObject.receiveLeft, ReceiveCallback, newClientObject); 
                lock (clients)
                {
                    clients.Add(newClientObject);
                }
                Console.WriteLine("Connected: " + newClientObject.remoteAddress + ", Total: " + clients.Count);
            }
            catch (Exception e)
            {
                if (Running)
                {
                    Console.WriteLine("Connection callback exception: " + e.Message);
                }
            }
            if (Running)
            {
                tcpListener.BeginAcceptTcpClient(ConnectionCallback, listenerObject);
            }
        }

        private void ReceiveCallback(IAsyncResult ar)
        {
            ClientObject clientObject = (ClientObject)ar.AsyncState;
            try
            {
                int bytesReceived = clientObject.tcpClient.GetStream().EndRead(ar);
                if (bytesReceived == 0)
                {
                    Thread.Sleep(10);
                }
                if (bytesReceived > 0)
                {
                    clientObject.lastReceive = DateTime.UtcNow.Ticks;
                    clientObject.receiveLeft -= bytesReceived;
                    if (clientObject.receiveLeft == 0)
                    {
                        if (!clientObject.receivingPayload)
                        {
                            using (MessageReader mr = new MessageReader(clientObject.receiveBuffer))
                            {
                                int receiveTypeInt = mr.Read<int>();
                                int receiveLengthInt = mr.Read<int>();
                                //Max 5MB
                                if (Enum.IsDefined(typeof(MessageType), receiveTypeInt) && receiveLengthInt >= 0 && receiveLengthInt < 5000000)
                                {
                                    clientObject.receiveType = (MessageType)receiveTypeInt;
                                    if (receiveLengthInt == 0)
                                    {
                                        clientObject.receiveBuffer = null;
                                        HandleMessage(clientObject);
                                        //Reset buffer
                                        clientObject.receiveBuffer = new byte[8];
                                        clientObject.receiveLeft = 8;
                                    
                                    }
                                    else
                                    {
                                        clientObject.receivingPayload = true;
                                        clientObject.receiveBuffer = new byte[receiveLengthInt];
                                        clientObject.receiveLeft = receiveLengthInt;
                                    }
                                }
                                else
                                {
                                    Console.WriteLine("Client " + clientObject.remoteAddress + " is not a port-checker client.");
                                    byte[] sendBytes = Encoding.UTF8.GetBytes("Disconnected non port-checker client.\n");
                                    clientObject.tcpClient.GetStream().Write(sendBytes, 0, sendBytes.Length);
                                    DisconnectClient(clientObject);
                                }
                            }
                        }
                        else
                        {
                            HandleMessage(clientObject);
                            clientObject.receivingPayload = false;
                            clientObject.receiveBuffer = new byte[8];
                            clientObject.receiveLeft = 8;
                        }
                    }
                }
                if (clientObject.tcpClient != null)
                {
                    clientObject.tcpClient.GetStream().BeginRead(clientObject.receiveBuffer, clientObject.receiveBuffer.Length - clientObject.receiveLeft, clientObject.receiveLeft, ReceiveCallback, clientObject);
                }
            }
            catch
            {
                DisconnectClient(clientObject);
            }
        }

        private void HandleMessage(ClientObject client)
        {
            try
            {
                switch (client.receiveType)
                {
                    case MessageType.HEARTBEAT:
                        Console.WriteLine("Heartbeat from " + client.remoteAddress);
                        break;
                    case MessageType.REQUEST_CHECK:
                        HandleRequestCheck(client);
                        break;
                    case MessageType.DISCONNECT:
                        DisconnectClient(client);
                        break;
                    default:
                        Console.WriteLine("Unhandled message type " + client.receiveType + " from " + client.remoteAddress);
                        DisconnectClient(client);
                        break;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Error handling client " + client.remoteAddress + ", Exception: " + e);
                DisconnectClient(client);
            }
        }

        private void HandleRequestCheck(ClientObject client)
        {
            using (MessageReader mr = new MessageReader(client.receiveBuffer))
            {
                int portToCheck = mr.Read<int>();
                Console.WriteLine("Checking port " + portToCheck + " at " + client.remoteAddress);
                TcpClient testClient = new TcpClient(AddressFamily.InterNetworkV6);
                IAsyncResult ar = testClient.BeginConnect(client.remoteAddress, portToCheck, CheckClient, null);
                byte[] replyBytes;
                using (MessageWriter mw = new MessageWriter())
                {
                    mw.Write<int>((int)MessageType.REPLY_CHECK);
                    mw.Write<int>(8);
                    mw.Write<int>(portToCheck);

                    if (ar.AsyncWaitHandle.WaitOne(CONNECTION_TIMEOUT))
                    {
                        if (testClient.Connected)
                        {
                            Console.WriteLine(portToCheck + " at " + client.remoteAddress + " is OPEN");
                            mw.Write<int>((int)ReplyType.OPEN);
                            replyBytes = mw.GetMessageBytes();
                            client.tcpClient.GetStream().Write(replyBytes, 0, replyBytes.Length);
                            testClient.EndConnect(ar);
                            testClient.Close();
                        }
                        else
                        {
                            mw.Write<int>((int)ReplyType.CLOSED);
                            replyBytes = mw.GetMessageBytes();
                            client.tcpClient.GetStream().Write(replyBytes, 0, replyBytes.Length);
                            Console.WriteLine(portToCheck + " at " + client.remoteAddress + " is CLOSED");
                        }
                    }
                    else
                    {
                        mw.Write<int>((int)ReplyType.FILTER);
                        replyBytes = mw.GetMessageBytes();
                        client.tcpClient.GetStream().Write(replyBytes, 0, replyBytes.Length);
                        Console.WriteLine(portToCheck + " at " + client.remoteAddress + " is FILTERED");
                    }
                }
            }
        }

        private void CheckClient(IAsyncResult ar)
        {
            //Don't care.
        }

        private void DisconnectClient(ClientObject client)
        {
            lock (clients)
            {
                if (clients.Contains(client))
                {
                    try
                    {
                        if (client.tcpClient != null)
                        {
                            client.tcpClient.Close();
                        }
                        client.tcpClient = null;
                    }
                    catch
                    {
                        //Don't care.
                    }
                    clients.Remove(client);
                    Console.WriteLine("Disconnected: " + client.remoteAddress + ", Total: " + clients.Count);
                }
            }
        }

        public void Stop()
        {
            if (Running)
            {
                Running = false;
                foreach (ClientObject client in clients.ToArray())
                {
                    try
                    {
                        DisconnectClient(client);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Error disconnecting client, exception: " + e.Message);
                    }
                }
                tcpListener.Stop();
                tcpListener = null;
            }
        }
    }
}

