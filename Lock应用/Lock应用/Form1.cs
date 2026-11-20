using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Lock应用
{
    public partial class Form1 : Form
    {
        private const string ServerIp = "192.168.215.37";
        private const int ServerPort = 60000;
        private const int MessageCount = 20;

        private readonly object _sendLock = new object();
        private readonly object _fileLock = new object();
        private readonly string _fileName = Path.Combine(Application.StartupPath, "data.txt");

        private TcpClient _client;
        private NetworkStream _stream;

        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            Connect();
        }

        private void button1_Click(object sender, EventArgs e) => Send(false);
        private void button2_Click(object sender, EventArgs e) => Send(true);
        private void button3_Click(object sender, EventArgs e) => WriteFile(false);
        private void button4_Click(object sender, EventArgs e) => WriteFile(true);

        private void Connect()
        {
            try
            {
                CloseConnection();

                _client = new TcpClient();

                Task connect = _client.ConnectAsync(ServerIp, ServerPort);
                if (!connect.Wait(3000))
                {
                    throw new TimeoutException("连接服务器超时。");
                }

                _stream = _client.GetStream();
            }
            catch (Exception ex)
            {
                CloseConnection();
                MessageBox.Show("连接服务器失败：" + ex.GetBaseException().Message);
            }
        }

        private void CloseConnection()
        {
            if (_stream != null)
            {
                try { _stream.Dispose(); } catch { }
                _stream = null;
            }

            if (_client != null)
            {
                try { _client.Close(); } catch { }
                _client = null;
            }
        }

        private void Send(bool useLock)
        {
            if (_stream == null)
            {
                Connect();
                if (_stream == null)
                {
                    return;
                }
            }

            try
            {
                Task.WaitAll(
                    Task.Run(() => SendWorker("A", useLock)),
                    Task.Run(() => SendWorker("B", useLock)));

                MessageBox.Show(
                    useLock ? "使用 Lock" : "未使用",
                    "发送完成");
            }
            catch (Exception ex)
            {
                CloseConnection();
                MessageBox.Show("发送失败：" + ex.GetBaseException().Message);
            }
        }

        private void SendWorker(string sender, bool useLock)
        {
            for (int i = 1; i <= MessageCount; i++)
            {
                if (useLock)
                {
                    lock (_sendLock)
                    {
                        WriteParts(sender, i);
                    }
                }
                else
                {
                    WriteParts(sender, i);
                }

                Thread.Sleep(1);
            }
        }

        // 每条消息拆成三段发送
        private void WriteParts(string sender, int index)
        {
            Write(sender + index.ToString("00") + ":");
            Thread.Sleep(2);
            Write(new string(sender[0], 10));
            Thread.Sleep(2);
            Write(Environment.NewLine);
        }

        private void Write(string text)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(text);
            _stream.Write(buffer, 0, buffer.Length);
        }

        private void WriteFile(bool useLock)
        {
            try
            {
                File.WriteAllText(_fileName, string.Empty);

                using (var writer = new StreamWriter(_fileName, true, Encoding.UTF8) { AutoFlush = true })
                {
                    Task.WaitAll(
                        Task.Run(() => WriteLines(writer, "A", useLock)),
                        Task.Run(() => WriteLines(writer, "B", useLock)));
                }

                MessageBox.Show(
                    "文件：" + _fileName + Environment.NewLine + Environment.NewLine +
                    File.ReadAllText(_fileName),
                    useLock ? "写入完成 - 使用 Lock" : "写入完成 - 未使用 Lock");
            }
            catch (Exception ex)
            {
                MessageBox.Show("写入失败：" + ex.GetBaseException().Message);
            }
        }

        private void WriteLines(StreamWriter writer, string sender, bool useLock)
        {
            for (int i = 1; i <= MessageCount; i++)
            {
                if (useLock)
                {
                    lock (_fileLock)
                    {
                        WriteLineParts(writer, sender, i);
                    }
                }
                else
                {
                    WriteLineParts(writer, sender, i);
                }

                Thread.Sleep(1);
            }
        }

        // 一整行拆成三段写入
        private static void WriteLineParts(StreamWriter writer, string sender, int index)
        {
            writer.Write(sender + index.ToString("00") + ":");
            Thread.Sleep(2);
            writer.Write(new string(sender[0], 10));
            Thread.Sleep(2);
            writer.WriteLine();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            CloseConnection();
            base.OnFormClosed(e);
        }
    }
}
