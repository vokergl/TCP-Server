using System;
using System.Text;
using System.Net.Sockets;
using System.Net;
using System.Threading;
using System.IO;

using System.Reflection;
using System.Runtime.InteropServices;
using System.Diagnostics;

namespace HTTPServer
{
    class Server
    {
        private static string[] UserList;                       //  Список пользователей, чьи запросы будут обрабатываться.
		private static string[] UserSession;                    //  Последние сессии пользователей.

		private static bool[] Processing;						//	Хранит в себе данные о пользователях, запросивших расчёт.
		private static int[] Queue;								//	Очередь, хранит порядок пользователей в очереди запросов.

		private static string path = "GTO.exe";					//  Путь к "GTO.exe".
		private static string disk = "";						//  Диск, на котором запущена КУДА.
		private static string CudaPath = "";					//  Путь к КУДА-модулю.

        private static int default_port = 17787;                //  Порт по умолчанию.
        private static int port = default_port;                 //  Порт, на котором работает сервер (считывается из "server.ini").

        private static int default_threads = 16;                //  Максимальное количество одновременных соединений по умолчанию.
        private static int max_threads = 64;                    //  Верхняя граница количества одновременных соединений.

        private static int threads = default_threads;           //  Максимальное количество одновременных соединений.

        private static int default_max_request_size = 2048;     //  Максимальный размер передаваемых пакетов по умолчанию.
        private static int max_request_size = 2048;             //  Максимальный размер передаваемых пакетов (считывается из "server.ini").
        private static int min_request_size = 32;               //  Минимальный размер запроса для расчётов.

        private static int Users = 0;                           //  Количество загруженных пользователей.
        private static bool ShowUsers = true;                   //  Нужно ли отображать загруженных пользователей при запуске.
		private static bool hide = false;						//  Запускать GTO.exe в фоновом режиме. НЕ ГОТОВО.

        TcpListener Listener;                                   //  Объект, принимающий TCP-клиентов.

		[DllImport("user32.dll", SetLastError = true)]
        internal static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

        static bool DeleteFile(string path, bool first=true)
        {
			if(File.Exists(path))
			{
				try
				{
					File.Delete(path);

					if(File.Exists(path))
					{
						FileAttributes attributes = File.GetAttributes(path);
						
						if((attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
						{
							attributes = attributes & ~FileAttributes.ReadOnly;

							File.SetAttributes(path, attributes);
						}

						if(first)
						{
							return DeleteFile(path, false);
						}

						Console.Write("Cannot Delete File " + path + ":\n\n");

						return false;
					}
					else
					{
						return true;
					}
				}
				catch(Exception Exception)
				{
					if(first)
					{
						FileAttributes attributes = File.GetAttributes(path);

						if((attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
						{
							attributes = attributes & ~FileAttributes.ReadOnly;

							File.SetAttributes(path, attributes);
						}

						return DeleteFile(path, false);
					}

					Console.Write("Cannot Delete File " + path + ":\n\n" + Exception + "\n");

					return false;
				}
			}
			else
			{
				return true;
			}
        }

		static bool RemoveDirectory(string path)
		{
			DirectoryInfo parent = new DirectoryInfo(path);

			bool flag = true;

			foreach(FileInfo file in parent.GetFiles())
			{
				if(DeleteFile(file.FullName) == false)
				{
					flag = false;
				}
			}

			foreach(DirectoryInfo directory in parent.GetDirectories())
			{
				directory.Attributes = FileAttributes.Normal;

				if(RemoveDirectory(directory.FullName) == false)
				{
					flag = false;
				}
			}

			parent.Attributes = FileAttributes.Normal;

			try
			{
				Directory.Delete(path);
			}
			catch(Exception Exception)
			{
				Console.WriteLine(Exception);
			}

			if(Directory.Exists(path))
			{
				flag = false;
			}

			return flag;
		}

		static string ReadFile(string path)
		{
			FileStream fstream = null;

			try
			{
			    fstream = new FileStream(path, FileMode.Open, FileAccess.Read);
			}
			catch
			{
			    return null;
			}

			byte[] Buffer = new byte[fstream.Length];

			int BytesToRead = (int)fstream.Length;
			int BytesRead = 0;

			while(BytesToRead > 0)
			{
			    int block = 0;

			    try
			    {
			        block = fstream.Read(Buffer, BytesRead, BytesToRead);
			    }
			    catch
			    {
			        return null;
			    }

			    if(block == 0)
			    { 
			        break;
			    }

			    BytesRead += block;
			    BytesToRead -= block;
			}

			try
			{
			    fstream.Flush();
				fstream.Close();
			}
			catch
			{
			    return null;
			}

			try
			{
				string Result = Encoding.ASCII.GetString(Buffer, 0, BytesRead);

				return Result;
			}
			catch
			{
				return null;
			}
		}

		static string ReadFile(string path, Encoding Encoding)
		{
			FileStream fstream = null;

			try
			{
			    fstream = new FileStream(path, FileMode.Open, FileAccess.Read);
			}
			catch
			{
			    return null;
			}

			byte[] Buffer = new byte[fstream.Length];

			int BytesToRead = (int)fstream.Length;
			int BytesRead = 0;

			while(BytesToRead > 0)
			{
			    int block = 0;

			    try
			    {
			        block = fstream.Read(Buffer, BytesRead, BytesToRead);
			    }
			    catch
			    {
			        return null;
			    }

			    if(block == 0)
			    { 
			        break;
			    }

			    BytesRead += block;
			    BytesToRead -= block;
			}

			try
			{
			    fstream.Flush();
				fstream.Close();
			}
			catch
			{
			    return null;
			}

			try
			{
				string Result = Encoding.GetString(Buffer, 0, BytesRead);

				return Result;
			}
			catch
			{
				return null;
			}
		}

		static bool WriteFile(string path, string str)
		{
			if(path == null || str == null)
			{
				return false;
			}

			byte[] Buffer = Encoding.ASCII.GetBytes(str);

            FileStream fstream = null;

            try
            {
                fstream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            }
            catch(Exception)
            {
                try
				{
					fstream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write);
				}
				catch
				{
					return false;
				}
            }

			try
			{
				fstream.Write(Buffer, 0, Buffer.Length);
			}
			catch
			{
				return false;
			}

			try
			{
				fstream.Flush();
				fstream.Close();
			}
			catch
			{

			}

			return true;
		}

		static bool WriteFile(string path, string str, Encoding Encoding)
		{
			if(path == null || str == null)
			{
				return false;
			}

			byte[] Buffer = Encoding.GetBytes(str);

            FileStream fstream = null;

            try
            {
                fstream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            }
            catch(Exception)
            {
                try
				{
					fstream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write);
				}
				catch
				{
					return false;
				}
            }

			try
			{
				fstream.Write(Buffer, 0, Buffer.Length);
			}
			catch
			{
				return false;
			}

			try
			{
				fstream.Flush();
				fstream.Close();
			}
			catch
			{

			}

			return true;
		}

		static void FlushQueue()
		{
			//	Освобождает и сдвигает очередь.

			//Queue[0] = Users;

			for(int i=0; i<Users-1; ++i)
			{
				Queue[i] = Queue[i+1];
			}

			Queue[Users-1] = Users;
		}

		class Client
        {
            private static string processing(string Request)
            {
                if(Request.Length < min_request_size)
                {
                    return "400 Bad Request";
                }

                //  Получаем имя пользователя и пытаемся найти его в списке:

                string User = Request.Substring(0, 16);

                bool find = false;
                int UserIndex = 0;

                for(int i=0; i<Users; ++i)
                {
                    if(UserList[i].Equals(User))
                    {
                        find = true;
                        UserIndex = i;
                        break;
                    }
                }

                if(find)
                {
					//	Пользователь найден.

					string UserDirectory = "Users\\" + UserList[UserIndex] + "\\";

					//	Сверим текущую сессию пользователя с сохранённой на сервере, чтобы избежать повторного расчёта:

					string Session = Request.Substring(16, 8);

					if(UserSession[UserIndex].Equals(Session))
					{
						//	В случае совпадения сессии, повторно высылаем "Decision.ini":

						string Decision = ReadFile(UserDirectory + "Decision.ini");

						if(Decision == null)
						{
							return "500 Internal Server Error";
						}
						else
						{
							return Decision;
						}
					}
					else
					{
						//	Старый "Decision.ini" нам больше не нужен.

						if(DeleteFile(UserDirectory + "Decision.ini") == false)
						{
							Console.WriteLine("Cannot Delete Decision.ini in " + UserDirectory);

							return "500 Internal Server Error";
						}
					}

					//	Обновляем информацию об активности пользователя:

					Processing[UserIndex] = true;

					//	Посчитаем количество пользователей, которые ждут расчёт:

					int Count = 0;

					for(int i=0; i<Users; ++i)
					{
						if(Processing[i])
						{
							++Count;
						}
					}

					//	Обновим информацию о нагруженности системы:

					IniFile Settings = new IniFile("Users\\" + UserList[UserIndex] + "\\GTO\\GTO_Settings.ini");

					Settings.Write("transit", Count.ToString(), "GTO");

					//	Встаём в очередь:

					for(int i=0; i<Users; ++i)
					{
						if(Queue[i] >= Users)
						{
							Queue[i] = UserIndex;

							break;
						}
					}

					//	Проверяем, запущен ли "GTO.exe" и КУДА:

					Check(UserIndex);
					CudaCheck();

					//	Удалим старый "needCalc.ini":

					if(DeleteFile(UserDirectory + "needCalc.ini") == false)
					{
						Console.WriteLine("Cannot Delete needCalc.ini in " + UserDirectory);
					}

					//	Ждём своей очереди:

					while(Queue[0] != UserIndex)
					{
						Thread.Sleep(100);
					}
					
                    //  Записываем "needCalc.ini" в папку пользователя:

                    string needCalc = Request.Substring(24, Request.Length - 24);

                    if(WriteFile(UserDirectory + "needCalc.ini", needCalc, Encoding.Unicode) == false)
					{
						Console.WriteLine("Cannot Write to needCalc.ini in " + UserDirectory);

						Processing[UserIndex] = false;

						FlushQueue();

						return "500 Internal Server Error";
					}

					//	Скопируем "needCalc.ini" в папку "Switch":

					for(int i=0; i<3; ++i)
					{
						try
						{
							if(DeleteFile(UserDirectory + "Switch\\needCalc.ini") == false)
							{
								if(i >= 2)
								{
									Console.WriteLine("Cannot Delete to needCalc.ini in " + UserDirectory + "Switch");

									Processing[UserIndex] = false;
																	
									FlushQueue();

									return "500 Internal Server Error";
								}								
							}

							File.Copy(UserDirectory + "needCalc.ini", UserDirectory + "Switch\\needCalc.ini");

							if(File.Exists(UserDirectory + "Switch\\needCalc.ini"))
							{
								break;
							}
						}
						catch(Exception Exception)
						{
							if(i == 2)
							{
								Console.WriteLine(Exception);

								Processing[UserIndex] = false;

								FlushQueue();

								return "500 Internal Server Error";
							}

							Thread.Sleep(50);
						}

						Thread.Sleep(50);
					}

					//  Отметим в файле "ProcessInfo.ini", что необходимо выполнить расчёт для данного пользователя:

					IniFile IniFile = new IniFile(UserDirectory + "ProcessInfo.ini");

                    IniFile.Write("processed", "false", "Calc");

                    Thread.Sleep(250);

                    bool Ready = false;

                    int counter = 1;
					int border = 65;

                    //  Дожидаемся готовности расчёта:

                    while(counter < border)
					{
                        if(bool.TryParse(IniFile.Read("processed", "Calc"), out Ready) == false)
                        {
							Ready = false;
                        }                        

                        if(Ready)
                        {
                            break;
                        }

                        Thread.Sleep(100);

                        ++counter;
                    }

                    if(counter == border)
                    {
						if(DeleteFile(UserDirectory + "Switch\\needCalc.ini") == false)
						{
							Console.WriteLine("Cannot Delete needCalc.ini in " + UserDirectory);
						}

                        //  Время Ожидания превышено.

						Processing[UserIndex] = false;

						FlushQueue();

                        return "417 Expectation Failed";
                    }

					//	Скопируем "Switch\\Decision.ini" в папку пользователя:

					for(int i=0; i<3; ++i)
					{
						try
						{
							if(DeleteFile(UserDirectory + "Decision.ini") == false)
							{
								if(i >= 2)
								{
									Console.WriteLine("Cannot Delete Decision.ini in" + UserDirectory);

									Processing[UserIndex] = false;

									FlushQueue();

									return "417 Expectation Failed";
								}
							}

							File.Copy(UserDirectory + "Switch\\Decision.ini", UserDirectory + "Decision.ini");

							if(File.Exists(UserDirectory + "Decision.ini"))
							{
								break;
							}
						}
						catch(Exception Exception)
						{
							if(i >= 2)
							{
								Console.WriteLine(Exception);

								Processing[UserIndex] = false;

								FlushQueue();

								return "500 Internal Server Error";
							}

							Thread.Sleep(50);
						}
					}

                    //  Расчёт завершён, читаем файл "Decision.ini":

                    string Responce = ReadFile(UserDirectory + "Decision.ini");
					
					if(Responce == null)
					{
						Processing[UserIndex] = false;

						FlushQueue();

						return "500 Internal Server Error";
					}

					//	Если всё прошло хорошо, отметим эту сессию как последнюю:

					UserSession[UserIndex] = Session;
					Processing[UserIndex] = false;

					FlushQueue();

                    return Responce;
                }
                else
                {
                    //  Пользователя нет в базе.

                    return "503 Service Unavailable.\n\n";
                }
            }

            public Client(TcpClient Client)
            {
                //  Узнаём IP-адрес клиента:
                //  IPAddress IpClient = ((IPEndPoint)Client.Client.LocalEndPoint).Address;

                //  Объявим строку, в которой будет хранится запрос клиента:
                string Request = "";

                //  Временный буфер для хранения принятых от клиента данных:
                byte[] Buffer = new byte[max_request_size];

                //  Переменная для хранения количества байт, принятых от клиента:
                int recd = 0;

                try
                {
                    //  Считываем принятые данные в Buffer:
                    //  Максимальный размер запроса определяется параметром max_request_size.

                    recd = Client.GetStream().Read(Buffer, 0, max_request_size);
                }
                catch(Exception Exception)
                {
                    Console.WriteLine(Exception);

                    //  В случае ошибки безопасно закрывем соединение:

                    try
                    {
                        Client.Close();
                    }
                    catch(Exception)
                    {

                    }

                    return;
                }

                try
                {
                    //  Преобразуем массив байтов в ASCII строку:

                    Request = Encoding.ASCII.GetString(Buffer, 0, recd);
                }
                catch(Exception Exception)
                {
                    Console.WriteLine(Exception);

                    //  В случае ошибки безопасно закрывем соединение:

                    try
                    {
                        Client.Close();
                    }
                    catch(Exception)
                    {

                    }

                    return;
                }

                //Console.WriteLine("Received " + recd + " bytes.");
                //Console.WriteLine(Request);

                //  Обработаем запрос пользователя:
                string Response = processing(Request);

                //  Подготавливаем ответ сервера:

				byte[] ResponseBuffer = Encoding.ASCII.GetBytes(Response);

                try
                {
                    //  Посылаем ответ клиенту:

                    Client.GetStream().Write(ResponseBuffer, 0, ResponseBuffer.Length);
                }
                catch(Exception Exception)
                {
                    //Console.WriteLine(Exception);
                }

                try
                {
                    Client.Close();
                }
                catch(Exception)
                {

                }
            }
        }
        
        public Server()
        {
            Listener = new TcpListener(IPAddress.Any, port);    //  Создаем "слушателя" для указанного порта.
            Listener.Start();                                   // Запускаем "слушателя.

            while(true)
            {
                //  Принимаем новых клиентов. После того, как клиент был принят, он передается в новый поток (ClientThread) с использованием пула потоков.
                
                ThreadPool.QueueUserWorkItem(new WaitCallback(ClientThread), Listener.AcceptTcpClient());
            }
        }

        static void ClientThread(Object StateInfo)
        {
            // Создаем новый экземпляр класса Client и передаем ему приведенный к классу TcpClient объект StateInfo.

            new Client((TcpClient)StateInfo);
        }
        
        //  Остановка сервера:
        ~Server()
        {
            if(Listener != null)
            {
                Listener.Stop();    //  Если "слушатель" был создан, остановим его.
            }
        }
        class IniFile
        {
            string Path;
            string EXE = Assembly.GetExecutingAssembly().GetName().Name;

            [DllImport("kernel32", CharSet = CharSet.Unicode)]
            static extern long WritePrivateProfileString(string Section, string Key, string Value, string FilePath);

            [DllImport("kernel32", CharSet = CharSet.Unicode)]
            static extern int GetPrivateProfileString(string Section, string Key, string Default, StringBuilder RetVal, int Size, string FilePath);

            public IniFile(string IniPath = null)
            {
                Path = new FileInfo(IniPath ?? EXE + ".ini").FullName.ToString();
            }

            public string Read(string Key, string Section = null)
            {
                StringBuilder RetVal = new StringBuilder(255);

                GetPrivateProfileString(Section ?? EXE, Key, "", RetVal, 255, Path);
                return RetVal.ToString();
            }

            public void Write(string Key, string Value, string Section = null)
            {
                WritePrivateProfileString(Section ?? EXE, Key, Value, Path);
            }

            public void DeleteKey(string Key, string Section = null)
            {
                Write(Key, null, Section ?? EXE);
            }

            public void DeleteSection(string Section = null)
            {
                Write(null, null, Section ?? EXE);
            }

            public bool KeyExists(string Key, string Section = null)
            {
                return Read(Key, Section).Length > 0;
            }
        }
        private static void LoadUserData(IniFile IniFile)
        {
            //  Функция для загрузки данных пользователей из ini-файла.

            //  dd4a368c297bad87 - Стандартный пользователь.

            if(bool.TryParse(IniFile.Read("show", "USERS"), out ShowUsers) == false)
            {
                ShowUsers = true;

                IniFile.Write("show", "true", "USERS");
            }

            if(int.TryParse(IniFile.Read("count", "USERS"), out Users) == false)
            {
                //  Не удалось загрузить список пользователей:

                Console.WriteLine("Cannot load User List");

                IniFile.Write("count", "1", "USERS");
                IniFile.Write("user1", "dd4a368c297bad87", "USERS");

				UserList = new string[1];

                UserList[0] = "dd4a368c297bad87";

                Users = 1;
            }
            else
            {
                UserList = new string[Users];

                int StartValue = Users;

                for(int i=0; i<StartValue; ++i)
                {
                    string User = IniFile.Read("user" + (i+1), "USERS");

                    bool flag = true;

                    for(int j=0; j<i; ++j)
                    {
                        //  Проверяем на совпадения имён пользователей:

                        try
                        { 
                            if(UserList[j].Equals(User))
                            {
                                flag = false;

                                break;
                            }
                        }
                        catch
                        {
                            flag = false;

                            break;
                        }
                    }

                    if(User.Length > 1 && flag)
                    {
                        //  Если всё хорошо, добавляем пользователя в базу.

                        UserList[i] = User;
                    }
                    else
                    {
                        --Users;
                    }
                }
            }

			UserSession = new string[Users];
			Processing = new bool[Users];
			Queue = new int [Users];

			for(int i=0; i<Users; ++i)
			{
				UserSession[i] = "null";
				Processing[i] = false;
				Queue[i] = Users;
			}

            IniFile.Write("count", Users.ToString(), "USERS");
        }
        private static bool CreateUserDirectories()
        {
            //  Создаём папку "Users":

            if(Directory.Exists("Users") == false)
            {
                try
                {
                    Directory.CreateDirectory("Users");
                }
                catch (Exception)
                {
                    Console.WriteLine("Cannot Create User Directory");

                    Console.ReadKey();

                    return false;
                }
            }

			//	Закроем все процессы GTO.exe и процесс КУДЫ, которые могли остаться от старых запусков:

			Process[] ProcessesList = Process.GetProcesses();

			foreach(Process Proc in ProcessesList)
			{
				for(int i=0; i<Users; ++i)
				{
					if(Proc.MainWindowTitle.Contains(UserList[i]))
					{
						Proc.Kill();
					}

					if(Proc.MainWindowTitle.Contains(CudaPath))
					{
						Proc.Kill();
					}
				}
			}

            //  Для каждого пользователя создаём свою папку UserList[i]:

			string dir = Environment.CurrentDirectory;

            for(int i=0; i<Users; ++i)
            {
                if(Directory.Exists("Users\\" + UserList[i]) == false)
                {
                    try
                    {
                        Directory.CreateDirectory("Users\\" + UserList[i]);
                    }
                    catch(Exception)
                    {
                        Console.WriteLine("Cannot Create User Directory for user " + UserList[i]);

                        Console.ReadKey();

                        return false;
                    }
                }

				//	Создаём папку "GTO" и копируем туда "GTO.exe":

				try
                {
					if(Directory.Exists("Users\\" + UserList[i] + "\\GTO") == false)
					{
						Directory.CreateDirectory("Users\\" + UserList[i] + "\\GTO");
					}
					else
					{
						if(DeleteFile("Users\\" + UserList[i] + "\\GTO\\GTO_Settings.ini") == false)
						{
							Console.WriteLine("Cannot Delete GTO_Settings.ini for User " + UserList[i]);
						}
					}
                }
                catch
                {
                    Console.WriteLine("Cannot Create GTO Directory for User " + UserList[i]);

                    Console.ReadKey();

                    return false;
                }

				if(File.Exists("Users\\" + UserList[i] + "\\GTO\\" + UserList[i] + ".exe"))
				{
					if(DeleteFile("Users\\" + UserList[i] + "\\GTO\\" + UserList[i] + ".exe") == false)
					{
						Console.WriteLine("Cannot Delete GTO.exe for User " + UserList[i]);
					}
				}

				try
                {
					File.Copy("GTO.exe", "Users\\" + UserList[i] + "\\GTO\\" + UserList[i] + ".exe");
				}
                catch(Exception)
                {
                    Console.WriteLine("Cannot Copy GTO.exe to GTO Directory for User " + UserList[i]);

                    Console.ReadKey();

                    return false;
                }

				try
                {
                    Directory.CreateDirectory("Users\\" + UserList[i] + "\\Switch");
                }
                catch(Exception)
                {
                    Console.WriteLine("Cannot Create Switch Directory for User " + UserList[i]);

                    Console.ReadKey();

                    return false;
                }

				//	Создаём папку "logs" и файл "Info.ini" для хранения логов:

				try
                {
					if(Directory.Exists("Users\\logs") == false)
					{
						Directory.CreateDirectory("Users\\logs");
					}
                }
                catch
                {
                    Console.WriteLine("Cannot Create logs Directory");

                    Console.ReadKey();

                    return false;
                }

				if(File.Exists("Users\\logs\\Info.ini") == false)
				{
					IniFile logs = new IniFile("Users\\logs\\Info.ini");

					logs.Write("dir", "1", "GTO");
				}
            }

			for(int i=0; i<Users; ++i)
			{				
				//	Устанавливаем настройки в "GTO_Settings.ini" и запускаем отдельные экземпляры "GTO.exe" для всех пользователей:

				SetIniFile(i);

				if(Start(i) == false)
				{
					Console.WriteLine("Cannot Start GTO.exe for User " + UserList[i]);

					return false;
				}
			}

			if(CudaStart() == false)
			{
				Console.WriteLine("Cannot Start Cuda-Module");

				return false;
			}

            return true;
        }
		private static void SetIniFile(int UserIndex)
		{
			string dir = Environment.CurrentDirectory;

			dir += "\\";	//	###

			IniFile Settings = new IniFile("Users\\" + UserList[UserIndex] + "\\GTO\\GTO_Settings.ini");

			Settings.Write("path", dir+"Users\\" + UserList[UserIndex], "GTO");

			Settings.Write("pathIniNeedCalc", dir+"Users\\" + UserList[UserIndex] + "\\Switch\\needCalc.ini", "GTO");
			Settings.Write("pathIniDecsion", dir+"Users\\" + UserList[UserIndex] + "\\Switch\\Decision.ini", "GTO");

			Settings.Write("pathIniNeedCalcNewFile", dir+"Users\\" + UserList[UserIndex] + "\\Switch\\needCalc.ini", "GTO");			
			Settings.Write("pathIniDecsionNewFile", dir+"Users\\" + UserList[UserIndex] + "\\Switch\\Decision.ini", "GTO");

			Settings.Write("pathIniLogDir", dir+"Users\\logs\\", "GTO");
			Settings.Write("pathWinGuestIni", dir+"GuestWin\\SessionInfo.ini", "GTO");

			Settings.Write("CheckDownsOneStreet", "true", "GTO");
			Settings.Write("StrategyFromRegrets", "true", "GTO");
			Settings.Write("AllMultiplierOff", "true", "GTO");
			Settings.Write("timeCalcMillisec", "1000", "GTO");

			Settings.Write("approximationHsEqIsOn", "false", "GTO");
			Settings.Write("TrainAccuracy", "false", "GTO");
			Settings.Write("iterationsRunEv", "3000", "GTO");
			Settings.Write("groupsHS", "20", "GTO");
			Settings.Write("groupsEQ", "20", "GTO");
			
			Settings.Write("uniqueRandomForApprox", "false", "GTO");
			Settings.Write("commonProbGroupForApprox", "false", "GTO");
			Settings.Write("dirTestLog", "", "GTO");

			Settings.Write("RulesModeHSandEQ", "true", "GTO");
			Settings.Write("printEqGroupsStrategy", "true", "GTO");
			Settings.Write("hPushOff", "false", "GTO");

			Settings.Write("CudaMode", "true", "Cuda");
			Settings.Write("disk", disk, "Cuda");
			Settings.Write("hide", hide.ToString(), "GTO");
		}

		private static bool Start(int UserIndex)
		{
			//	Запускает экземпляр "GTO.exe" для пользователя UserList[UserIndex].

			string dir = Environment.CurrentDirectory;

			Process Proc = new Process();

			//	dir + "Users\\" ###

			Proc.StartInfo.FileName = dir + "\\Users\\" + UserList[UserIndex] + "\\GTO\\" + UserList[UserIndex] + ".exe";
			Proc.StartInfo.UseShellExecute = true;
			Proc.StartInfo.WorkingDirectory = dir + "\\Users\\" + UserList[UserIndex] + "\\GTO\\";
			
			bool flag = false;

			try
			{
				flag = Proc.Start();
			}
			catch(Exception Exception)
			{
				flag = false;

				Console.WriteLine(Exception);
			}

			return flag;
		}

		private static bool CudaStart()
		{
			//	Запускает КУДА-модуль.

			Process Proc = new Process();

			Proc.StartInfo.FileName = CudaPath;
			Proc.StartInfo.UseShellExecute = true;
			Proc.StartInfo.WorkingDirectory = disk + "HuGtoIteration";
			
			bool flag = false;

			try
			{
				flag = Proc.Start();
			}
			catch(Exception Exception)
			{
				flag = false;

				Console.WriteLine(Exception);
			}

			return flag;
		}

		private static void Check(int Userindex)
		{
			//	Проверяет, запущен ли экземпляр "GTO.exe" для текущего пользователя.

			Process[] ProcessesList = Process.GetProcesses();

			bool find = false;

			foreach(Process Proc in ProcessesList)
			{
				if(Proc.MainWindowTitle.Contains(UserList[Userindex]))
				{
					find = true;

					break;
				}
			}

			//	Запускает "GTO.exe", если он не запущен:

			if(find == false)
			{
				Start(Userindex);
			}
		}

		private static void CudaCheck()
		{
			//	Проверяет, запущен КУДА-модуль.

			Process[] ProcessesList = Process.GetProcesses();

			bool find = false;

			foreach(Process Proc in ProcessesList)
			{
				if(Proc.MainWindowTitle.Contains(Path.GetFileName(CudaPath)))
				{
					find = true;

					break;
				}
			}

			//	Запускает "GTO.exe", если он не запущен:

			if(find == false)
			{
				CudaStart();
			}
		}

        static void Main(string[] args)
        {
			#region ReadSettings

			//  Считываем настройки сервера из файла "server.ini":

			IniFile IniFile = new IniFile("server.ini");

            if(File.Exists("server.ini"))
            {
                if(int.TryParse(IniFile.Read("port", "SERVER"), out port) == false)
                {
                    port = default_port;

                    IniFile.Write("port", default_port.ToString(), "SERVER");
                }

                if(port < 80 || port > 65535)
                {
                    IniFile.Write("port", default_port.ToString(), "SERVER");

                    port = default_port;
                } 

                if(int.TryParse(IniFile.Read("threads", "SERVER"), out threads) == false)
                {
                    IniFile.Write("threads", default_threads.ToString(), "SERVER");

                    threads = default_threads;
                }

                if(threads < 2 || threads > max_threads)
                {
                    //  Определим оптимальное количество потоков, исходя из количества процессоров:

                    default_threads = Environment.ProcessorCount * 4;

                    IniFile.Write("threads", default_threads.ToString(), "SERVER");

                    threads = default_threads;
                }

                if(int.TryParse(IniFile.Read("block", "SERVER"), out max_request_size) == false)
                {
                    IniFile.Write("block", default_max_request_size.ToString(), "SERVER");

                    max_request_size = default_max_request_size;
                }

                if(max_request_size <= min_request_size)
                {
                    IniFile.Write("block", default_max_request_size.ToString(), "SERVER");

                    max_request_size = default_max_request_size;
                }

				//	Прочитаем из настроек путь к КУДЕ:

				string Parce = IniFile.Read("path", "Cuda");

				if(File.Exists(Parce) == false)
				{
					IniFile.Write("path", "", "Cuda");

					Console.Write("Cannot find Cuda-file.\n\n");

					Console.ReadKey();

					return;
				}

				try
				{
					CudaPath = Parce;

					disk = Parce.Substring(0, 1);
				}
				catch
				{
					try
					{
						disk = Environment.CurrentDirectory.Substring(0, 1);
					}
					catch
					{
						disk = "C";
					}
				}

				//	Прочитаем, следует ли запускать экземпляры "GTO.exe" в фоновом режиме:

				//if(bool.TryParse(IniFile.Read("hide", "GTO"), out hide) == false)
				//{
				//	hide = false;
				//}

				//IniFile.Write("hide", hide.ToString(), "GTO");
            }
            else
            {
                //  Определим оптимальное количество потоков, исходя из количества процессоров:

                default_threads = Environment.ProcessorCount * 4;

                //  Параметры по умолчанию:

                IniFile.Write("port", default_port.ToString(), "SERVER");
                IniFile.Write("threads", default_threads.ToString(), "SERVER");
                IniFile.Write("block", default_max_request_size.ToString(), "SERVER");

                IniFile.Write("count", "1", "USERS");
                IniFile.Write("show", "true", "USERS");

                IniFile.Write("user1", "dd4a368c297bad87", "USERS");

				try
				{
					disk = Environment.CurrentDirectory.Substring(0, 1);
				}
				catch
				{
					disk = "C";
				}

				IniFile.Write("disk", disk, "Cuda");
				//IniFile.Write("hide", hide.ToString(), "GTO");
            }
            
			//	Считываем путь к "GTO.exe" из файла "server.ini":

			string temp = IniFile.Read("path", "GTO");

			if(File.Exists(temp))
			{
				path = temp;
			}
			else
			{
				if(File.Exists("GTO.exe") == false)
				{
					IniFile.Write("path", "", "GTO");

					Console.Write("Cannot find GTO.exe file.\n\n");

					Console.ReadKey();

					return;
				}
			}

			IniFile.Write("path", path, "GTO");

            //  Загружаем данные пользователей и создаём каталоги для дальнейшей работы:

            LoadUserData(IniFile);

            #endregion
            
            if(CreateUserDirectories() == false)
            {
                return;
            }

            if(Users > threads)
            {
                //  Увеличиваем количество потоков до количества пользователей:

                threads = Users;

                IniFile.Write("threads", threads.ToString(), "SERVER");
            }

            if(Users < 1)
            {
                Console.Write("No User has been Loaded.\n\nYou should check the \"server.ini\" file.\n\n");

                Console.ReadKey();

                return;
            }

            Console.Write("Starting Server on port " + port + " with " + threads + " threads and " + max_request_size + " bytes limit.\n\n");

            if(ShowUsers)
            {
                //  Отображаем количество загруженных в базу пользователей:
                //  Эту функцию можно отключить в "server.ini" [USERS] -> show = true.

                if(Users == 1)
                {
                    Console.Write("One User was Loaded:\n\n");
                }
                else
                {
                    Console.Write(Users + " Users were Loaded:\n\n");
                }

                for(int i=0; i<Users; ++i)
                {
                    Console.WriteLine("user" + (i+1) + " = " + UserList[i]);
                }
            }

            // Установим максимальное количество рабочих потоков:
            ThreadPool.SetMaxThreads(threads, threads);

            // Установим минимальное количество рабочих потоков:
            ThreadPool.SetMinThreads(2, 2);

            // Запускаем сервер:
            new Server();
        }
    }
}