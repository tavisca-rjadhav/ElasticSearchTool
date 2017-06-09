using Dynamitey;
using Microsoft.CSharp.RuntimeBinder;
using Nest;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Dynamic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace TopCitiesFinder
{
    class Program
    {
        private static ConcurrentDictionary<string, List<KeyValuePair<string, string>>> supplierUnmappedHotel = new ConcurrentDictionary<string, List<KeyValuePair<string, string>>>();
        private static object dictLock = new object();
        private static object listLock = new object();

        static void Main(string[] args)
        {
            Stopwatch watch = Stopwatch.StartNew();
            int numberOfDays = 3;

            // haveing one slot of 2 hrs
            var numberOfSlots = numberOfDays * 12;

            var slots = new List<DateTime>();

            for (int i = 0; i < numberOfSlots; i++)
            {
                    slots.Add(DateTime.UtcNow.AddHours(-(2 * i)));
            }

            //foreach (var x in slots)
            //{
            //    UpdateAdditionalInfo(x, x.AddHours(-2));
            //}
            Parallel.ForEach(slots, (x) =>
             {
                 UpdateAdditionalInfo(x, x.AddHours(-2));
             }
            );

            Console.WriteLine($"done! Time taken {watch.ElapsedMilliseconds}");
            //File.WriteAllText("C:/unmappedHotels.txt", JsonConvert.SerializeObject(supplierUnmappedHotel));
            Console.ReadKey(true);
        }

        private static void UpdateAdditionalInfo(DateTime start, DateTime end)
        {

            try
            {
                var fileName = start.ToString("yyyy-MM-dd-HH-mm", CultureInfo.InvariantCulture);

                var node = new Uri("http://localhost:9200/");

                var settings = new ConnectionSettings(
                    node
                ).DefaultIndex("logs-*").DisableDirectStreaming();                

                var client = new ElasticClient(settings);

                var hourSlots = new List<DateTime>();

                int count = 0;
                int batchSize = 0;
                QueryContainer query = new TermQuery
                {
                    Field = "ContextIdentifier",
                    Value = "1bnzlrbkohs"
                };
              
                query = query && new DateRangeQuery
                {
                    Field = "Timestamp",
                    LessThan = start,
                    GreaterThan = end
                };

                query = query && new WildcardQuery
                {
                    Field = "AdditionalInfo.ProviderName",
                    Value = "*prod"
                };
            

                bool moreResults = true;

                while (moreResults)
                {
                    var searchRequest = new SearchRequest
                    {
                        From = count,
                        Size = 200,
                        Query = query
                    };
                    count = count + 200;
                    var response = client.Search<RedisLogs>(searchRequest);
                    Console.WriteLine(fileName + "-" + response.Documents.Count);

                    if (response.Documents.Count == 0)
                        moreResults = false;
                    else
                        batchSize = batchSize + response.Documents.Count;                                        
                    foreach (var request in response.Hits)
                    {

                        try
                        {          

                            if (request.Source.AdditionalInfo.ContainsKey("ProviderName"))
                            {
                                var supplierName = request.Source.AdditionalInfo["ProviderName"];
                                if (supplierName.Equals("tourico prod"))
                                {
                                    var addInfo = request.Source.AdditionalInfo;
                                    addInfo["ProviderName"] = "tourico";                                 
                                 
                                    var updateResponse = client.Update<RedisLogs, LogPartial>(DocumentPath<RedisLogs>.Id(request.Id), descriptor => descriptor
                                        .Index(request.Index).Type(request.Type)
                                        .Doc(new LogPartial
                                        {
                                            AdditionalInfo = addInfo
                                        }));
                                }
                            }
                        }
                        catch (Exception ex)
                        {

                        }
                    }

            
                }


            }
            catch (Exception ex)
            {               
                throw;
            }

        }

        private static bool isBodyPresent(dynamic usgRequest)
        {
            try
            {
                var x = usgRequest.Body;

                return x != null;
            }
            catch (RuntimeBinderException)
            {
                return false;
            }
        }
    }


    class RedisLogs
    {
        public string ContextIdentifier { get; set; }
        public string Response { get; set; }
        public string ApplicationName { get; set; }
        public string CallType { get; set; }
        public String Id { get; set; }
        public String Type { get; set; }
        public DateTime TimeStamp { get; set; }
        public Dictionary<string, string> AdditionalInfo { get; set; }
    }

    class LogPartial
    {
        [JsonProperty("AdditionalInfo")]
        public Dictionary<string, string> AdditionalInfo { get; set; }
    }
}
