using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using CoreTweet;
using System.Xml.Serialization;

namespace GetTweetFromJson
{

    public static class CoreTweetExtend
    {

        private static int token_index=0;

        /// <summary>
        /// UserTimelineAPIを使う。もしAPIを使いきっているなら、スリープし、再実行する。
        /// </summary>
        /// <param name="statuses"></param>
        /// <param name="sleepTime"></param>
        /// <param name="report"></param>
        /// <param name="parameters"></param>
        /// <returns></returns>
        public static IEnumerable<CoreTweet.Status> UserTimelineRetry(this List<Tokens> tokens, TimeSpan sleepTime, Action<string> report, params System.Linq.Expressions.Expression<Func<string, object>>[] parameters)
        {
            CoreTweet.Status[] list2 = new List<CoreTweet.Status>().ToArray();
            bool flag = false;
            try
            {
                list2 = tokens[token_index].Statuses.UserTimeline(parameters).ToArray();
            }
            catch (Exception ex)
            {
                report(ex.Message);
                if (ex.Message == "Rate limit exceeded")
                {
                    flag = true;
                }
                else if (ex.Message == "Over capacity")
                {
                    flag = true;
                }
                else
                {
                    throw ex;
                }

            }
            if (flag)
            {
                report("Sleep");
                System.Threading.Thread.Sleep(sleepTime);
                token_index = (token_index + 1) % tokens.Count;
                report("Token is " + token_index.ToString());
                Program.nowTokenIndex = token_index;
                return UserTimelineRetry(tokens, sleepTime, report, parameters);
            }
            return list2;
        }

        /// <summary>
        /// 指定したscreen_nameのユーザのツイートを指定個数取得する。(Retryあり)
        /// </summary>
        /// <param name="token"></param>
        /// <param name="user_id"></param>
        /// <param name="maxCount"></param>
        /// <param name="sleep"></param>
        /// <param name="report"></param>
        /// <returns></returns>
        public static IEnumerable<Status> GetUserTimeLines(List<Tokens> tokens, ulong user_id, int maxCount, TimeSpan sleep, Action<string> report)
        {
            var list2 = tokens.UserTimelineRetry(sleep, report, id => user_id, count => 100, exclude_replies => true, include_rts => false);
            int c = 0;
            long maxId = long.MaxValue;
            foreach (var item in list2)
            {
                maxId = item.Id;
                c++;
                yield return item;
            }
            while (true)
            {
                list2 = tokens.UserTimelineRetry(sleep, report, id => user_id, count => 100, max_id => (maxId - 1).ToString(), exclude_replies => true, include_rts => false);
                int c1 = 0;
                foreach (var item in list2)
                {
                    maxId = item.Id;
                    yield return item;
                    c++;
                    c1++;
                    if (maxCount <= c) break;
                }
                if (c1 < 20) break;
                if (maxCount <= c) break;
            }
        }
    }
    public class TwitterAPI
    {

        public string APIKey = "";
        public string APISecret = "";
        public string AccessToken = "";
        public string AccessTokenSecret = "";
        private static List<TwitterAPI> myApis;

        public TwitterAPI(string APIKey, string APISecret, string AccessToken, string AccessTokenSecret)
        {
            this.APIKey = APIKey;
            this.APISecret = APISecret;
            this.AccessToken = AccessToken;
            this.AccessTokenSecret = AccessTokenSecret;
        }

        public TwitterAPI() { }

        /// <summary>
        /// ツイッターAPI情報のロード
        /// </summary>
        /// <returns>API情報</returns>
        public static List<TwitterAPI> loadAPIInfo()
        {
            List<TwitterAPI> APIList = new List<TwitterAPI>();
            XmlSerializer serializer = new XmlSerializer(typeof(TwitterAPI));
            StreamReader sr = new StreamReader(
                Directory.GetCurrentDirectory() + @"\twitterAPI.xml", new System.Text.UTF8Encoding(false));

            APIList.Add((TwitterAPI)serializer.Deserialize(sr));

            XmlSerializer serializer2 = new XmlSerializer(typeof(TwitterAPI));
            StreamReader sr2 = new StreamReader(
                Directory.GetCurrentDirectory() + @"\twitterAPI2.xml", new System.Text.UTF8Encoding(false));

            APIList.Add((TwitterAPI)serializer2.Deserialize(sr2));
            return APIList;
        }


        /// <summary>
        /// トークンの取得
        /// </summary>
        /// <returns>トークン</returns>
        public static List<Tokens> getTokens()
        {
            if (myApis == null)
            {
                myApis = loadAPIInfo();
            }

            List<Tokens> tokens = new List<Tokens>();

            foreach(var myApi in myApis)
            {
                tokens.Add(Tokens.Create(myApi.APIKey
                , myApi.APISecret
                , myApi.AccessToken
                , myApi.AccessTokenSecret));
            }
            return tokens;
        }

    }

    class userInfo
    {
        public userInfo(UserResponse user)
        {
            userId = user.Id.Value;
            CreatedAt = user.CreatedAt.DateTime;
            Favorite = user.FavouritesCount;
            Follow = user.FriendsCount;
            Follower = user.FollowersCount;
            StatusesCount = user.StatusesCount;
            isDefaultProfile = user.IsDefaultProfile;
            isDefaultProfileImage = user.IsDefaultProfileImage;
        }

        public string ToString()
        {
            return string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11}",
                 userId, CreatedAt.ToString("yyyy/MM/dd HH:mm:ss"), Favorite, Follow, Follower, StatusesCount, isDefaultProfile, isDefaultProfileImage,
                RTMean, RTDev, FavMean, FavDev);
        }

        public userInfo() { }

        DateTime CreatedAt;
        long userId;
        int Favorite, Follow, Follower, StatusesCount;
        bool isDefaultProfile, isDefaultProfileImage;
        public double RTMean, RTDev, FavMean, FavDev;
    }


    class Program
    {
        public static int nowTokenIndex = 0;
        static void Main(string[] args)
        {
            //StreamReader sr = new StreamReader(@"account_csv.log");
            StreamReader sr = new StreamReader(args[0]);
            bool isCrawTrainData = true;
            string imagePath = "images";
            string corpusPath = "corpus";
            System.Net.WebClient wc = new System.Net.WebClient();

            ulong savedId = Properties.Settings.Default.Last_ID;
            bool isLoad = false;
            if (savedId != 0) isLoad = true;

            var tokens = TwitterAPI.getTokens();
            System.Text.RegularExpressions.Regex r =
    new System.Text.RegularExpressions.Regex(@"https?://[\w/:%#\$&\?\(\)~\.=\+\-]+");

            //画像保存用
            if (!Directory.Exists(imagePath) && isCrawTrainData) Directory.CreateDirectory(imagePath);
            //文書保存用
            if (!Directory.Exists(corpusPath) && isCrawTrainData) Directory.CreateDirectory(corpusPath);

            //ループ
            while (sr.Peek() > -1)
            {
                string id_str = sr.ReadLine().Split(',')[0];
                ulong account_id = Convert.ToUInt64(id_str);
                bool unMedia = true;

                //goto saved pointer
                if (isLoad && savedId != account_id) continue;
                if (savedId == account_id)
                {
                    isLoad = false;
                    continue;
                }

                Console.WriteLine(id_str+":");
                UserResponse user=null;

                //UserResponce取得
                userFirst:
                try
                {
                    user = tokens[nowTokenIndex].Users.Show(id => account_id);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(("user:" + ex.Message));
                    if (ex.Message == "Rate limit exceeded")
                    {
                        System.Threading.Thread.Sleep(new TimeSpan(0, 1, 0));
                        goto userFirst;
                    }
                    else if (ex.Message == "Over capacity")
                    {
                        System.Threading.Thread.Sleep(new TimeSpan(0, 1, 0));
                        goto userFirst;
                    }
                }

                try
                {
                    uint counter = Properties.Settings.Default.counter;
                    List<int> RTList = new List<int>(), FavList = new List<int>();
                    List<string> myCorpus = new List<string>();
                    List<int> CorpusRT = new List<int>();
                    List<long> CorpusID = new List<long>();
                    List<string> CorpusTime = new List<string>();

                    //user info
                    userInfo uf = new userInfo(user);

                    //get tweet
                    var lines = CoreTweetExtend.GetUserTimeLines(tokens, account_id, 3000, new TimeSpan(0, 1, 0), (n) => Console.WriteLine(n));

                    //画像用ファイル
                    string myImagePath = Path.Combine(imagePath, user.Id.Value.ToString());
                    if (!Directory.Exists(myImagePath) && isCrawTrainData) Directory.CreateDirectory(myImagePath);

                    foreach (var str in lines)
                    {
                        RTList.Add(str.RetweetCount.Value);
                        FavList.Add(str.FavoriteCount.Value);

                        //Mediaのみ取得
                        if (str.Entities.Media != null || !isCrawTrainData)
                        {
                            //文章手直し
                            string replaced = r.Replace(str.Text, "");
                            replaced = replaced.Replace("\r", "");
                            replaced = replaced.Replace("\n", "");
                            if (isCrawTrainData)
                            {
                                replaced = replaced.Replace(",", "、");
                                replaced = replaced.Replace("．", "。");
                            }

                            if (replaced != "")
                            {
                                myCorpus.Add(replaced);
                                CorpusRT.Add(str.RetweetCount.Value);
                                CorpusID.Add(str.Id);
                                CorpusTime.Add(str.CreatedAt.ToString("yyyy/MM/dd HH:mm:ss"));

                                //メディア保存
                                if (isCrawTrainData)
                                {
                                    unMedia = false;
                                    string tweetPath = Path.Combine(myImagePath, str.Id.ToString());
                                    Directory.CreateDirectory(tweetPath);
                                    foreach(var media in str.Entities.Media)
                                    {
                                        string thisPath = Path.Combine(tweetPath, Path.GetFileName(media.MediaUrl));
                                        wc.DownloadFile(media.MediaUrl, thisPath);

                                        Console.WriteLine(thisPath+":"+counter);
                                    }
                                }
                            }
                        }
                    }

                    if (!unMedia)
                    {

                        //統計計算
                        uf.RTMean = RTList.Average();
                        uf.RTDev = Math.Sqrt(RTList.Select(t => Math.Pow(t - uf.RTMean, 2.0)).Sum() / RTList.Count());
                        uf.FavMean = FavList.Average();
                        uf.FavDev = Math.Sqrt(FavList.Select(t => Math.Pow(t - uf.FavMean, 2.0)).Sum() / FavList.Count());

                        //ユーザ情報書き込み
                        if (isCrawTrainData)
                        {
                            using (StreamWriter sw_ui = new StreamWriter("user_info.txt", true, Encoding.UTF8))
                            {
                                sw_ui.WriteLine(uf.ToString());
                            }
                        }

                        //コーパス書き込み
                        string mycpPath = Path.Combine(corpusPath, user.Id + ".txt");
                        using (StreamWriter cw_cp = new StreamWriter(mycpPath, true, Encoding.UTF8))
                        {
                            foreach (string cp in myCorpus.Zip(CorpusID, (first, second) => string.Format("{0},{1}", first, second)).Zip(CorpusRT, (first, second) => string.Format("{0},{1}", first, second)))
                            {
                                cw_cp.WriteLine(cp);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
                Properties.Settings.Default.Last_ID = account_id;
                Console.WriteLine(Properties.Settings.Default.counter);
                Properties.Settings.Default.counter++;
                Properties.Settings.Default.Save();
            }
        }
    }
}
