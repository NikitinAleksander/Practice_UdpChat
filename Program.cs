using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.IO;
using System.Xml.Serialization;
using System.Diagnostics;

namespace Chat
{
    public class Program
    {
        static IPAddress remoteAddress = IPAddress.Parse("235.5.5.11");
        const int remotePort = 8001; 
        const int localPort = 8001; 
        static string username;
        
        [Serializable] 
        public class FileDetails
        {
            public string FILETYPE = "";
            public long FILESIZE = 0;
        }

        private static FileDetails fileDet = new FileDetails();
        private static UdpClient sender = new UdpClient();
        private static IPEndPoint endPoint= new IPEndPoint(remoteAddress, remotePort);
        private static FileStream fs;
        private static FileStream history = new FileStream("history.txt", FileMode.OpenOrCreate, FileAccess.Write, FileShare.Write);
        private static Byte[] receiveBytes = new Byte[0];
        private static UdpClient receiver = new UdpClient(localPort);
        private static IPEndPoint remoteIp = null;
        private static int CountFile = 1;

        static void Main(string[] args)
        {
            try
            {
                Console.Write("Введите свое имя:");
                username = Console.ReadLine();
                remoteAddress = IPAddress.Parse("235.5.5.11");
                Thread receiveThread = new Thread(new ThreadStart(ReceiveMessage));
                receiveThread.Start();
                Thread sendThread=new Thread(new ThreadStart(SendMessage));
                sendThread.Start();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }
        private static void SendMessage()
        {
            UdpClient sender = new UdpClient(); 
            IPEndPoint endPoint = new IPEndPoint(remoteAddress, remotePort);
            try
            {
                while (true)
                {
                    string message = Console.ReadLine();
                    if (message == "file")
                    {
                        try
                        {
                            // Получаем путь файла и его размер 
                            Console.WriteLine("Введите путь к файлу и его имя");
                            fs = new FileStream(@Console.ReadLine().ToString(), FileMode.Open, FileAccess.Read);
                            
                            message = String.Format("{0}: {1}", username, message);
                            byte[] data = Encoding.UTF8.GetBytes(message);
                            sender.Send(data, data.Length, endPoint);
                            // Отправляем информацию о файле
                            SendFileInfo();

                            // Ждем 2 секунды
                            Thread.Sleep(2000);

                            // Отправляем сам файл
                            SendFile();

                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex.ToString());
                        }
                    }
                    else
                    {
                        message = String.Format("{0}: {1}", username, message);
                        byte[] data = Encoding.UTF8.GetBytes(message+"\n");
                        sender.Send(data, data.Length, endPoint);
                        history.Write(data, 0, data.Length);
                        history.Flush();
                                              
                    }
                    
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            finally
            {
                sender.Close();
            }
        }
        private static void ReceiveMessage()
        {
            receiver.JoinMulticastGroup(remoteAddress, 20);
            IPEndPoint remoteIp = null;
            string localAddress = LocalIPAddress();
            try
            {
                while (true)
                {
                    byte[] data = receiver.Receive(ref remoteIp); 
                    if (remoteIp.Address.ToString().Equals(localAddress))
                        continue;
                    string message = Encoding.UTF8.GetString(data);
                    history.Write(data, 0, data.Length);
                    history.Flush();
                    if (message.Split(':')[1]== " file")
                    {
                        // Получаем информацию о файле
                        GetFileDetails();

                        // Получаем файл
                        ReceiveFile();
                    }
                    else
                    {
                        Console.WriteLine(message);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            finally
            {
                receiver.Close();
            }
        }
        private static string LocalIPAddress()
        {
            string localIP = "";
            IPHostEntry host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (IPAddress ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    localIP = ip.ToString();
                    break;
                }
            }
            return localIP;
        }

        public static void SendFileInfo()
        {

            // Получаем тип и расширение файла
            fileDet.FILETYPE = fs.Name.Substring((int)fs.Name.Length - 3, 3);

            // Получаем длину файла
            fileDet.FILESIZE = fs.Length;

            XmlSerializer fileSerializer = new XmlSerializer(typeof(FileDetails));
            MemoryStream stream = new MemoryStream();

            // Сериализуем объект
            fileSerializer.Serialize(stream, fileDet);

            // Считываем поток в байты
            stream.Position = 0;
            Byte[] bytes = new Byte[stream.Length];
            stream.Read(bytes, 0, Convert.ToInt32(stream.Length));

            Console.WriteLine("--Отправка деталей файла...");

            // Отправляем информацию о файле
            sender.Send(bytes, bytes.Length, endPoint);
            stream.Close();

        }

        private static void SendFile()
        {
            // Создаем файловый поток и переводим его в байты
            Byte[] bytes = new Byte[fs.Length];

            Console.WriteLine("--Отправка файла размером " + fs.Length + " байт");
            try
            {
                int i = 0;
                fs.Read(bytes, 0, bytes.Length);

                // Отправляем файл пакетами по 8 кб
                while (fs.Length - i * 8192>0)
                {
                    if (fs.Length - i * 8192 > 8192)
                    {
                        Byte[] bytes_ = new Byte[8192];
                        for(int j=0;j< 8192; j++)
                        {
                            bytes_[j] = bytes[i * 8192 + j];
                        }
                        sender.Send(bytes_, bytes_.Length, endPoint);
                        i++;
                    }
                    else
                    {
                        Byte[] bytes_end = new Byte[fs.Length - i * 8192];
                        for (int j = 0; j < bytes_end.Length; j++)
                        {
                            bytes_end[j] = bytes[i * 8192 + j];
                        }
                        sender.Send(bytes_end, bytes_end.Length, endPoint);
                        i++;
                    }
                }
                
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
            finally
            {
                // Очищаем поток
                fs.Close();
            }
            Console.WriteLine("--Файл отправлен.");
        }

        private static void GetFileDetails()
        {
            try
            {
                Console.WriteLine("--Ожидание информации о файле");
                // Получаем информацию о файле
                receiveBytes = receiver.Receive(ref remoteIp);
                Console.WriteLine("--Информация о файле получена!");

                XmlSerializer fileSerializer = new XmlSerializer(typeof(FileDetails));
                MemoryStream stream1 = new MemoryStream();

                // Считываем информацию о файле
                stream1.Write(receiveBytes, 0, receiveBytes.Length);
                stream1.Position = 0;

                // Вызываем метод Deserialize
                fileDet = (FileDetails)fileSerializer.Deserialize(stream1);
                Console.WriteLine("--Будет получен файл типа ." + fileDet.FILETYPE +
                    " имеющий размер " + fileDet.FILESIZE.ToString() + " байт");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }

        public static void ReceiveFile()
        {
            try
            {
                Console.WriteLine("--Ожидайте получение файла");

                // Создаем временный файл с полученным расширением
                fs = new FileStream("file"+CountFile.ToString()+"." + fileDet.FILETYPE, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite);
                CountFile++;
                // Получаем файл и записываем его
                int i = 0;
                while (fileDet.FILESIZE - i * 8192 > 0)
                {
                    receiveBytes = receiver.Receive(ref remoteIp);
                    fs.Write(receiveBytes, 0, receiveBytes.Length);
                    i++;
                }

                Console.WriteLine("--Файл получен и сохранен");
                
                Console.WriteLine("--Открытие файла");

                // Открываем файл связанной с ним программой
                Process.Start(fs.Name);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
            finally
            {
                fs.Close();
            }
        }
    }
}