using Prometheus;
using System;
using System.Threading;
using Neo4j.Driver;
using System.Diagnostics;
using Newtonsoft.Json;

class Program
{

    public class Metric
    {
        public string name { get; set; }
        public string description { get; set; }
        public string query { get; set; }
        public int count { get; set; }
        public bool istest { get; set; }
        public Gauge metric { get; set; }
    };

    public class Settings
    {
        public int port { get; set; }
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
            Console.WriteLine("Error executing request..." + ex.Message);
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

    static void Main()
    {
        Gauge connmetric = Metrics.CreateGauge("neo4j_connections_count", "Number of active connections to server");
        Gauge tranmetric = Metrics.CreateGauge("neo4j_transaction_count", "Number of active transactions on server");
        Settings appsettings = JsonConvert.DeserializeObject<Settings>(File.ReadAllText("settings.json"));
        List<Metric> metricslist = JsonConvert.DeserializeObject<List<Metric>>(File.ReadAllText("metrics.json"));
        foreach(Metric mt in metricslist)
        {
            mt.metric = Metrics.CreateGauge(mt.name, mt.description);
        }

        var server = new KestrelMetricServer(port: appsettings.port);
        server.Start();

        while (true)
        {
            try
            {
                _driver = GraphDatabase.Driver("neo4j://" + appsettings.neoaddress, AuthTokens.Basic(appsettings.neouser , appsettings.neopassword));
                using (var session = _driver.Session())
                {
                    int testdelay = 0;
                    Console.WriteLine("Exporter started...");
                    while (true)
                    {
                        // query log
                        IResult res = session.Run("show transactions");
                        int numtran = 0;
                        foreach (var record in res)
                        {
                            string query = record["currentQuery"].ToString();
                            if (query != "show transactions")
                            {
                                if (query.Trim().Length > 0)
                                {
                                    File.AppendAllText("querylog.txt", DateTime.Now.ToString() + ": " + query + "\n");
                                    numtran++;
                                }
                            }
                        }
                        tranmetric.Set(numtran);

                        // number of connections
                        IResult res2 = session.Run("CALL dbms.listConnections()");
                        int numrecords = res2.Count();
                        connmetric.Set(numrecords);

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

                        testdelay = testdelay + appsettings.delay;
                        Thread.Sleep(TimeSpan.FromSeconds(appsettings.delay));
                    }
                }
            }
            catch(Exception ex)
            {
                Thread.Sleep(TimeSpan.FromSeconds(120));
                Console.WriteLine("Exception with connection..." + ex.Message);
            }
        }
    }
}
