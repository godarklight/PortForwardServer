using System;

namespace PortForwardServer
{
    public class MainClass
    {
        public static void Main()
        {
            ServerSettings serverSettings = new ServerSettings("settings.txt");
            PortServer portServer = new PortServer(serverSettings);
            portServer.Run();
            Console.WriteLine("Press any key to exit");
            Console.ReadKey();
            portServer.Stop();
            Console.WriteLine("Goodbye!");
        }
    }
}

