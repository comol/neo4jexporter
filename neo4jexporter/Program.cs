using Prometheus;
using System;
using System.Threading;
using Neo4j.Driver;
using System.Diagnostics;
using Newtonsoft.Json;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Text;
using System.Net;

class Program
{

    public class Metric
    {
        public string name { get; set; }
        public string description { get; set; }
        public string query { get; set; }
        public int count { get; set; }
        public bool istest { get; set; }
        public bool enabled { get; set; }
        public Gauge metric { get; set; }
    };

    public class Settings
    {
        public int port { get; set; }
        public int httpport { get; set; }
        public int delay { get; set; }
        public int testdelaymultiply { get; set; }
        public string neoaddress { get; set; }
        public string neouser { get; set; }
        public string neopassword { get; set; }

    };

    private static IDriver _driver;
    private static System.Diagnostics.Stopwatch watch = new System.Diagnostics.Stopwatch();

    static void AddMetric(Gauge Counter, string Request, int count, ISession session)
    {
        int num = 0;
       
        try
        {
            watch.Start();
            var rez = session.Run(Request);
            num = rez.Count(); //for real query execution 
            watch.Stop();
        }
        catch (Exception ex)
        {
            Counter.Set(-1);
            watch.Reset();
            File.AppendAllText("log.txt", DateTime.Now.ToString() + ": Error executing request..." + ex.Message + " \n");
            return;
        }

        if (num != count)
        { 
            Counter.Set(-1);
        }
        else
        {
            Counter.Set(watch.ElapsedMilliseconds);
        }   
        
        watch.Reset();
    }

    static int CountTransactions(ISession session)
    {
        int numtran = 0;

        for (int i = 0; i < 3; i++) 
        {        
            IResult res = session.Run("show transactions");
            foreach (var record in res)
            {
                string query = record["currentQuery"].ToString();
                if (query != "show transactions")
                {
                    if (query.Trim().Length > 0)
                    {
                        File.AppendAllText("querylog.txt", DateTime.Now.ToString() + ": " + query + "\n");
                        numtran = 1;
                    }
                }
            }
            Thread.Sleep(300);
        }
        return numtran;
    }

    static void Main()
    {
        List<Metric> prevmetricslist = new List<Metric>();
        List<Metric> metricslist = new List<Metric>();
        Settings appsettings = new Settings();

        //start http server to read logs
        Thread HttpThread = new Thread(LogListener);
        HttpThread.Start();

        // standard metrics
        Gauge connmetric = Metrics.CreateGauge("neo4j_connections_count", "Number of active connections to server");
        Gauge tranmetric = Metrics.CreateGauge("neo4j_transaction_count", "Number of active transactions on server");
        try
        {
            appsettings = JsonConvert.DeserializeObject<Settings>(File.ReadAllText("settings.json"));
        }
        catch (Exception ex)
        {
            File.AppendAllText("log.txt", DateTime.Now.ToString() + ": Exception while reading settings..." + ex.Message + " \n");
        }

        // prometheus server start
        var server = new KestrelMetricServer(port: appsettings.port);
        server.Start();

        // main circle to connect neo4j infinity
        while (true)
        {
            try
            {
                _driver = GraphDatabase.Driver("neo4j://" + appsettings.neoaddress, AuthTokens.Basic(appsettings.neouser , appsettings.neopassword));
                using (var session = _driver.Session())
                {
                    int testdelay = 0;
                    File.AppendAllText("log.txt", DateTime.Now.ToString() + ": Exporter Started... \n");
                    
                    // metrics circle
                    while (true)
                    {
                        // read metrics from file
                        try
                        {
                            prevmetricslist = JsonConvert.DeserializeObject<List<Metric>>(File.ReadAllText("metrics.json"));
                        }
                        catch (Exception ex)
                        {
                            File.AppendAllText("log.txt", DateTime.Now.ToString() + ": Exception while reading metrics list..." + ex.Message + " \n");
                            Thread.Sleep(TimeSpan.FromSeconds(120));
                            continue;
                        }

                        // clear metrics
                        foreach (Metric mt in metricslist)
                        {
                            mt.metric.Unpublish();
                        }
                        metricslist = new List<Metric>();
                        
                        // add only enabled metrics to list
                        foreach (Metric pmt in prevmetricslist)
                        {
                            if (pmt.enabled)
                            {
                                metricslist.Add(pmt);
                            }
                        }
                        
                        // add prometheus metrics
                        foreach (Metric mt in metricslist)
                        {                           
                            mt.metric = Metrics.CreateGauge(mt.name, mt.description);                            
                        }
                        
                        // number of transactions
                        int numtran = CountTransactions(session);
                        tranmetric.Set(numtran);

                        // number of connections
                        IResult res2 = session.Run("CALL dbms.listConnections()");
                        int numrecords = res2.Count();
                        connmetric.Set(numrecords);

                        //transactional metrics only when no transactions in neo
                        if (numtran == 0) { 

                            // base metrics
                            foreach (Metric mt in metricslist)
                            {
                                if (!mt.istest)
                                {
                                    Program.AddMetric(mt.metric, mt.query, mt.count, session);
                                }
                            }

                            // tests run with more delay
                            if(testdelay > appsettings.delay * appsettings.testdelaymultiply)
                            {
                                foreach (Metric mt in metricslist)
                                {
                                    if (mt.istest)
                                    {
                                        Program.AddMetric(mt.metric, mt.query, mt.count, session);
                                    }
                                }
                                testdelay = 0;
                            }
                        }

                        testdelay = testdelay + appsettings.delay;
                        Thread.Sleep(TimeSpan.FromSeconds(appsettings.delay));
                    }
                }
            }
            catch(Exception ex)
            {
                foreach (Metric mt in metricslist)
                {
                    mt.metric.Set(-1);
                }
                Thread.Sleep(TimeSpan.FromSeconds(120));
                File.AppendAllText("log.txt", DateTime.Now.ToString() + ": Exception with connection..." + ex.Message + " \n");
            }
        }
    }


    public static void LogListener()
    {
        Settings st = JsonConvert.DeserializeObject<Settings>(File.ReadAllText("settings.json"));
        HttpListener listener = new HttpListener();
        listener.Prefixes.Add("http://*:" + st.httpport.ToString() + "/D651F1C2CCF042BEA725C462286B2F82/");
        listener.Start();
        File.AppendAllText("log.txt", DateTime.Now.ToString() + ": Http listener started... \n");
        while (true)
        {
            HttpListenerContext context = listener.GetContext();
            HttpListenerRequest request = context.Request;
            HttpListenerResponse response = context.Response;
            //TODO или пагинация или хотя бы читать только концовку файла
            //string logtext = File.ReadLines("querylog.txt").TakeLast(1000).ToString();
            string logtext = File.ReadAllText("querylog.txt");
            
            byte[] buffer = System.Text.Encoding.UTF8.GetBytes(logtext);
            // Get a response stream and write the response to it.
            response.ContentLength64 = buffer.Length;
            System.IO.Stream output = response.OutputStream;
            output.Write(buffer, 0, buffer.Length);
            // You must close the output stream.
            output.Close();
        }
        listener.Stop();
        File.AppendAllText("log.txt", DateTime.Now.ToString() + ": Http listener ended... \n");
    }
}
