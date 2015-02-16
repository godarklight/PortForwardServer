using System;
using System.IO;

namespace PortForwardServer
{
    public class ServerSettings
    {
        public int Port
        {
            get;
            private set;
        }

        public ServerSettings(string settingsFile)
        {
            if (!File.Exists(settingsFile))
            {
                File.WriteAllText(settingsFile, "port = 9002");
            }
            LoadSettings(settingsFile);
        }

        public void LoadSettings(string settingsFile)
        {
            using (StreamReader sr = new StreamReader(settingsFile))
            {
                string currentLine;
                while ((currentLine = sr.ReadLine()) != null)
                {
                    if (currentLine.Contains("="))
                    {
                        string trimmedLine = currentLine.TrimStart();
                        string currentKey = trimmedLine.Substring(0, trimmedLine.IndexOf("=")).TrimEnd();
                        string currentValue = trimmedLine.Substring(trimmedLine.IndexOf("=") + 1).TrimStart();
                        //Console.WriteLine("Current key: '" + currentKey + "', current value: '" + currentValue + "'");
                        if (currentKey == "port")
                        {
                            Port = Int32.Parse(currentValue.Trim());
                        }
                    }
                }
            }
        }
    }
}

