using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Linq;

namespace PressureTool
{
    class Program
    {
        /// <summary>
        /// 名称：并发请求工具；作者：神牛步行3；邮箱：841202396@qq.com
        /// </summary>
        /// <param name="args"></param>
        static void Main(string[] args)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Console.OutputEncoding = Encoding.GetEncoding("GB2312");

            //var watchGet = Stopwatch.StartNew();
            //var result = HttpGet("https://www.zhihu.com/").Result;
            //watchGet.Stop();
            //Console.WriteLine($"耗时：{watchGet.ElapsedMilliseconds}ms；");

            //Console.ReadLine();
            //return;

            //Parallel
            Console.WriteLine($"名称：并发请求工具；作者：神牛步行3；邮箱：841202396@qq.com");
            var isExist = false;
            var isShowDetail = false;
            try
            {

                var baseConfPath = Path.Combine(Directory.GetCurrentDirectory(), "PressureTool.json");
                if (string.IsNullOrWhiteSpace(baseConfPath)) { Console.WriteLine($"加载配置文件：{baseConfPath}失败"); return; }

                do
                {
                    Console.WriteLine("******************************************************************");
                    Console.WriteLine($"{DateTime.Now.ToString("yyyy/MM/dd HH:mm")}：初始化工具，请稍后...");
                    Console.WriteLine(string.Format("配置文件：{0}，加载中...", baseConfPath));
                    //读取配置文件
                    var strConf = GetConf(baseConfPath).Result;
                    if (string.IsNullOrWhiteSpace(strConf)) { Console.WriteLine($"加载配置文件：内容为空！"); break; }
                    var baseConf = JsonConvert.DeserializeObject<MoToolConf>(strConf);

                    if (baseConf.MoTaskInfoes == null || baseConf.MoTaskInfoes.Count <= 0)
                    {
                        Console.WriteLine($"待执行任务：0个，请先配置任务节点。"); break;
                    }

                    baseConf.ResultLogPath = string.IsNullOrWhiteSpace(baseConf.ResultLogPath) ? Path.Combine(Directory.GetCurrentDirectory(), "ToolLog") : baseConf.ResultLogPath;
                    Console.WriteLine($"全局日志记录路径：{baseConf.ResultLogPath}");

                    //开启不同的多个父级任务
                    var len = baseConf.MoTaskInfoes.Count;
                    Console.WriteLine($"待执行任务：{len}个，正在执行中...\r\n=============================================================");
                    var baseTaskArr = new Task<bool>[len];
                    for (int j = 0; j < len; j++)
                    {
                        var taskInfo = baseConf.MoTaskInfoes[j];
                        baseTaskArr[j] = Task.Factory.StartNew<bool>((info) =>
                         {
                             var nowInfo = info as MoTaskInfo;
                             if (nowInfo == null || nowInfo.LinkNum <= 0 || string.IsNullOrWhiteSpace(nowInfo.Url)) { return false; }

                             var taskArr = new Task<HttpResponseMessage>[nowInfo.LinkNum];

                             var sbLog = new StringBuilder(string.Empty);
                             sbLog.AppendFormat("目标：{0}；方式：{1}；并发请求：{2}个；\r\n", nowInfo.Url, nowInfo.Method, nowInfo.LinkNum);

                             var childUseTimes = new List<long>();
                             var watch = Stopwatch.StartNew();
                             //开启某个待执行任务的并发请求
                             for (int i = 0; i < nowInfo.LinkNum; i++)
                             {
                                 taskArr[i] = Task.Factory.StartNew<HttpResponseMessage>((childInfo) =>
                                {
                                    var httResponse = new HttpResponseMessage();
                                    var nowChildInfo = childInfo as MoTaskInfo;
                                    var childWatch = Stopwatch.StartNew();
                                    switch (nowChildInfo.Method.ToLower())
                                    {
                                        case "httppost_xml":
                                            var contentXml = new StringContent(nowChildInfo.Param, Encoding.UTF8, "application/xml");
                                            httResponse = HttpPost(nowChildInfo.Url, contentXml).Result;
                                            break;
                                        case "httppost_json":
                                            var contentJson = new StringContent(nowChildInfo.Param, Encoding.UTF8, "application/json");
                                            httResponse = HttpPost(nowChildInfo.Url, contentJson).Result;
                                            break;
                                        default: //默认httpget
                                            httResponse = HttpGet(nowChildInfo.Url).Result;
                                            break;
                                    }
                                    childWatch.Stop();
                                    var useTime = childWatch.ElapsedMilliseconds;
                                    childUseTimes.Add(useTime);
                                    if (isShowDetail) { Console.WriteLine($"Url：{nowChildInfo.Url}，耗时：{useTime}ms"); }
                                    return httResponse;
                                }, nowInfo);
                             }
                             //完成
                             Task.WaitAll(taskArr);
                             watch.Stop();

                             //计数
                             var nSucc = taskArr.Count(b => b.Result.StatusCode == System.Net.HttpStatusCode.OK);
                             var nSucc_Val = Math.Round((nSucc * 100 * 10 / nowInfo.LinkNum) * 0.1, 1);  //*百分比100*扩大10倍   最后缩小10倍
                             var nFail = nowInfo.LinkNum - nSucc;  //taskArr.Count(b => b.Result.StatusCode != System.Net.HttpStatusCode.OK);
                             var nFail_Val = Math.Round(100 - nSucc_Val, 1);

                             var totalTime = watch.ElapsedMilliseconds;
                             var minUseTime = childUseTimes.Min();
                             var maxUseTime = childUseTimes.Max();
                             sbLog.AppendFormat("成功：{0}个；失败：{1}个；\r\n" +
                                                "最小耗时：{2}ms；最大耗时：{3}ms；任务完成耗时：{4}ms；\r\n" +
                                                "成功率：{5}%；失败率：{6}%；\r\n" +
                                                "=============================================================",
                                                 nSucc, nFail,
                                                 minUseTime, maxUseTime, totalTime,
                                                 nSucc_Val, nFail_Val);
                             Console.WriteLine(sbLog.ToString());

                             //记录文本日志
                             var resultLogPath = string.IsNullOrWhiteSpace(nowInfo.ResultLogPath) ? baseConf.ResultLogPath : nowInfo.ResultLogPath;
                             WriteLog(sbLog.ToString(), resultLogPath);
                             return true;
                         }, taskInfo);
                    }

                    Task.WaitAll(baseTaskArr);
                    Console.WriteLine("执行任务完成，是否继续执行。\r\n温馨提示输入：R(重新执行)；RD(查看每个请求耗时)；RS(关闭查看每个请求耗时)；N(结束程序)");

                    var inputCode = Console.ReadLine().ToUpper();
                    if (inputCode == "R") { }
                    else if (inputCode == "RD") { isShowDetail = true; }
                    else if (inputCode == "RS") { isShowDetail = false; }
                    else if (inputCode != "R") { isExist = true; break; }
                } while (true);
            }
            catch (Exception ex)
            {
                Console.WriteLine("异常信息：" + ex.Message);
            }
            if (!isExist) { Console.ReadKey(); }
        }

        #region 文件操作

        /// <summary>
        /// 读取配置文件
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        static async Task<string> GetConf(string path)
        {
            if (!File.Exists(path)) { return ""; }

            using (var stream = File.OpenRead(path))
            {
                using (StreamReader reader = new StreamReader(stream))
                {
                    return await reader.ReadToEndAsync();
                }
            }
        }

        /// <summary>
        /// 写文本日志
        /// </summary>
        /// <param name="content"></param>
        /// <param name="path"></param>
        /// <param name="fileName"></param>
        static void WriteLog(string content, string path = "", string fileName = "")
        {
            if (string.IsNullOrWhiteSpace(path)) { path = Directory.GetCurrentDirectory(); }
            else if (!Directory.Exists(path)) { Directory.CreateDirectory(path); }

            if (string.IsNullOrWhiteSpace(fileName)) { fileName = $"{DateTime.Now.ToString("HH")}.txt"; }
            fileName = fileName.ToLower().Contains(".txt") ? fileName : $"{fileName}.txt";

            path = Path.Combine(path, fileName);
            File.AppendAllText(
                path,
                $"{DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss.fff")}：\r\n{content}\r\n",
                Encoding.UTF8);
        }

        #endregion

        #region http请求

        /// <summary>
        /// httpPost请求
        /// </summary>
        /// <param name="url"></param>
        /// <param name="content"></param>
        /// <param name="nTimeOut"></param>
        /// <returns></returns>
        static async Task<HttpResponseMessage> HttpPost(string url, HttpContent content, int nTimeOut = 30)
        {
            var httpResponse = new HttpResponseMessage { StatusCode = System.Net.HttpStatusCode.NotFound };
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.BaseAddress = new Uri(url);
                    client.Timeout = new TimeSpan(0, 0, nTimeOut);
                    return await client.PostAsync(url, content);
                }
            }
            catch (Exception ex) { }
            return httpResponse;
        }

        /// <summary>
        /// httpget请求
        /// </summary>
        /// <param name="url"></param>
        /// <param name="nTimeOut"></param>
        /// <returns></returns>
        static async Task<HttpResponseMessage> HttpGet(string url, int nTimeOut = 30)
        {
            var httpResponse = new HttpResponseMessage { StatusCode = System.Net.HttpStatusCode.NotFound };
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.BaseAddress = new Uri(url);
                    client.Timeout = new TimeSpan(0, 0, nTimeOut);
                    httpResponse = await client.GetAsync(url);
                }
            }
            catch (Exception ex) { }
            return httpResponse;
        }
        #endregion

        #region 配置信息

        public class MoToolConf
        {
            /// <summary>
            /// 执行结果日志记录路径(全局，默认程序根目录)
            /// </summary>
            public string ResultLogPath { get; set; }

            public List<MoTaskInfo> MoTaskInfoes { get; set; }
        }

        public class MoTaskInfo
        {

            /// <summary>
            /// 请求方式，目前支持：httpget，httppost
            /// </summary>
            public string Method { get; set; }

            /// <summary>
            /// 请求地址
            /// </summary>
            public string Url { get; set; }

            /// <summary>
            /// 连接数
            /// </summary>
            public int LinkNum { get; set; }

            /// <summary>
            /// 参数（post使用）
            /// </summary>
            public string Param { get; set; }

            /// <summary>
            /// 执行结果日志记录路径（私有>全局）
            /// </summary>
            public string ResultLogPath { get; set; }
        }

        #endregion
    }
}