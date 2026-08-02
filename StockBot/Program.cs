// StockBot.cs - Naomi (نائومی) Telegram Trading Bot (Anti-Freeze SocketsHttpHandler + Thread-Safe Concurrency)
// تک‌فایل C# کامل - بدون هیچ دستوری که با / شروع شود
// مجهز به کانکشن‌پلینگ ضد فریز (TCP Keep-Alive)، قفل‌های هم‌زمانی ایمن، قیمت‌گذاری پویا و خرید/فروش هر تعداد واحد دلخواه

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using ScottPlot;
using IOFile = System.IO.File;

namespace StockBotApp
{
    public class StockBot
    {
        private static readonly string Token = "8871928516:AAGChm-ApCPvd53KZDD8pIr1CEfYISCcLqI";
        private static readonly long OwnerId = 8248899977;
        private static ITelegramBotClient Bot = null!;
        public static string BotUsername = "NaomiBot";

        public class PricePoint
        {
            public DateTime Timestamp { get; set; } = DateTime.UtcNow;
            public decimal Price { get; set; }
        }

        public class Currency
        {
            public string Symbol { get; set; } = "";
            public long TotalSupply { get; set; }
            public decimal BaseValue { get; set; }
            public long CirculatingSupply { get; set; }
            public string PhotoUrl { get; set; } = "";
            public string Description { get; set; } = "";
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
            public List<Order> Orders { get; set; } = new();
            public List<decimal> PriceHistory { get; set; } = new();
            public List<PricePoint> TimedPriceHistory { get; set; } = new();
        }

        public class Order
        {
            public long UserId { get; set; }
            public string Type { get; set; } = "";
            public decimal Price { get; set; }
            public long Quantity { get; set; }
            public DateTime Timestamp { get; set; }
        }

        public class User
        {
            public long UserId { get; set; }
            public string Username { get; set; } = "";
            public decimal Balance { get; set; } = 5000m;
            public Dictionary<string, long> Portfolio { get; set; } = new();
            public Dictionary<string, decimal> CostBasis { get; set; } = new();
            public List<long> DeviceFingerprints { get; set; } = new();
            public DateTime LastDailyReward { get; set; }

            // XP & Level System
            public int Level { get; set; } = 1;
            public int XP { get; set; } = 0;
            public int TotalTrades { get; set; } = 0;
            public int SuccessfulTrades { get; set; } = 0;
            public decimal TotalProfit { get; set; } = 0;
            public int CrisisSurvived { get; set; } = 0;

            // Referral
            public string ReferralCode { get; set; } = "";
            public int Referrals { get; set; } = 0;
            public bool HasUsedReferral { get; set; } = false;
            public bool IsBanned { get; set; } = false;
        }

        private static Dictionary<string, Currency> Market = new();
        private static Dictionary<long, User> Users = new();
        private static Dictionary<long, string> UserStates = new();

        // مسیر دیتابیس در دایرکتوری کاربر (/root) خارج از مخزن کلون‌شده تا پس از rm -rf Naomi هرگز پاک نشود
        private static readonly string DbPath = Environment.GetEnvironmentVariable("NAOMI_DB_PATH") ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "naomi_data.db");
        private static readonly string JsonPath = Environment.GetEnvironmentVariable("NAOMI_JSON_PATH") ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "naomi_data.json");

        private static bool _useSqlite = true;
        private static readonly object _dataLock = new();
        private static volatile bool _saveRequested = false;

        public static string FmtMoney(decimal amount) => amount == Math.Floor(amount) ? $"${amount:N0}" : $"${amount:#,##0.##}";
        public static string FmtPrice(decimal price) => price == Math.Floor(price) ? $"${price:N0}" : $"${price:#,##0.##}";

        public static string GetShamsiTehranTime(DateTime utcDate)
        {
            try
            {
                var tehranTz = TimeZoneInfo.FindSystemTimeZoneById("Iran Standard Time");
                var tehranDate = TimeZoneInfo.ConvertTimeFromUtc(utcDate, tehranTz);
                var pc = new System.Globalization.PersianCalendar();
                int year = pc.GetYear(tehranDate);
                int month = pc.GetMonth(tehranDate);
                int day = pc.GetDayOfMonth(tehranDate);

                string[] monthNames = { "", "فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور", "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند" };
                string mName = month >= 1 && month <= 12 ? monthNames[month] : month.ToString();

                return $"{day} {mName} {year} - ساعت {tehranDate:HH:mm} تهران";
            }
            catch
            {
                return utcDate.ToString("yyyy-MM-dd HH:mm UTC");
            }
        }

        private static void RequestSave()
        {
            _saveRequested = true;
        }

        private static SqliteConnection GetDbConnection()
        {
            var conn = new SqliteConnection($"Data Source={DbPath};");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                PRAGMA journal_mode=WAL;
                PRAGMA synchronous=NORMAL;

                CREATE TABLE IF NOT EXISTS Settings (
                    Key TEXT PRIMARY KEY,
                    Value TEXT
                );

                CREATE TABLE IF NOT EXISTS Currencies (
                    Symbol TEXT PRIMARY KEY,
                    Description TEXT,
                    BaseValue REAL,
                    TotalSupply INTEGER,
                    CirculatingSupply INTEGER,
                    PhotoUrl TEXT,
                    PriceHistoryJson TEXT,
                    CreatedAt TEXT,
                    TimedPriceHistoryJson TEXT
                );

                CREATE TABLE IF NOT EXISTS Users (
                    UserId INTEGER PRIMARY KEY,
                    Username TEXT,
                    Balance REAL,
                    Level INTEGER,
                    XP INTEGER,
                    TotalTrades INTEGER,
                    SuccessfulTrades INTEGER,
                    TotalProfit REAL,
                    CrisisSurvived INTEGER,
                    ReferralCode TEXT,
                    Referrals INTEGER,
                    LastDailyReward TEXT,
                    PortfolioJson TEXT,
                    CostBasisJson TEXT,
                    DeviceFingerprintsJson TEXT,
                    HasUsedReferral INTEGER DEFAULT 0
                );

                CREATE TABLE IF NOT EXISTS Orders (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Symbol TEXT,
                    UserId INTEGER,
                    Type TEXT,
                    Price REAL,
                    Quantity INTEGER,
                    Timestamp TEXT
                );
            ";
            cmd.ExecuteNonQuery();
            try
            {
                using var altCmd = conn.CreateCommand();
                altCmd.CommandText = "ALTER TABLE Users ADD COLUMN CostBasisJson TEXT;";
                altCmd.ExecuteNonQuery();
            }
            catch { }
            try
            {
                using var altCmd2 = conn.CreateCommand();
                altCmd2.CommandText = "ALTER TABLE Users ADD COLUMN HasUsedReferral INTEGER DEFAULT 0;";
                altCmd2.ExecuteNonQuery();
            }
            catch { }
            try
            {
                using var altCmd3 = conn.CreateCommand();
                altCmd3.CommandText = "ALTER TABLE Currencies ADD COLUMN CreatedAt TEXT;";
                altCmd3.ExecuteNonQuery();
            }
            catch { }
            try
            {
                using var altCmd4 = conn.CreateCommand();
                altCmd4.CommandText = "ALTER TABLE Currencies ADD COLUMN TimedPriceHistoryJson TEXT;";
                altCmd4.ExecuteNonQuery();
            }
            catch { }
            try
            {
                using var altCmd5 = conn.CreateCommand();
                altCmd5.CommandText = "ALTER TABLE Users ADD COLUMN IsBanned INTEGER DEFAULT 0;";
                altCmd5.ExecuteNonQuery();
            }
            catch { }
            return conn;
        }

        private static void SaveDataImmediate()
        {
            List<Currency> currenciesCopy;
            List<User> usersCopy;

            lock (_dataLock)
            {
                currenciesCopy = Market.Values.Select(c => new Currency
                {
                    Symbol = c.Symbol,
                    Description = c.Description,
                    BaseValue = c.BaseValue,
                    TotalSupply = c.TotalSupply,
                    CirculatingSupply = c.CirculatingSupply,
                    PhotoUrl = c.PhotoUrl,
                    CreatedAt = c.CreatedAt,
                    PriceHistory = new List<decimal>(c.PriceHistory),
                    TimedPriceHistory = new List<PricePoint>(c.TimedPriceHistory),
                    Orders = c.Orders.Select(o => new Order
                    {
                        UserId = o.UserId,
                        Type = o.Type,
                        Price = o.Price,
                        Quantity = o.Quantity,
                        Timestamp = o.Timestamp
                    }).ToList()
                }).ToList();

                usersCopy = Users.Values.Select(u => new User
                {
                    UserId = u.UserId,
                    Username = u.Username,
                    Balance = u.Balance,
                    Portfolio = new Dictionary<string, long>(u.Portfolio),
                    CostBasis = new Dictionary<string, decimal>(u.CostBasis),
                    DeviceFingerprints = new List<long>(u.DeviceFingerprints),
                    LastDailyReward = u.LastDailyReward,
                    Level = u.Level,
                    XP = u.XP,
                    TotalTrades = u.TotalTrades,
                    SuccessfulTrades = u.SuccessfulTrades,
                    TotalProfit = u.TotalProfit,
                    CrisisSurvived = u.CrisisSurvived,
                    ReferralCode = u.ReferralCode,
                    Referrals = u.Referrals,
                    HasUsedReferral = u.HasUsedReferral,
                    IsBanned = u.IsBanned
                }).ToList();
            }

            // اکنون قفل حافظه (_dataLock) کاملاً آزاد است و ربات با سرعت ۰ میلی‌ثانیه به تلگرام پاسخ می‌دهد!
            if (_useSqlite)
            {
                try
                {
                    SaveToSqliteSnapshot(currenciesCopy, usersCopy);
                    return;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Naomi] SQLite save error, switching to JSON fallback: {ex.Message}");
                    _useSqlite = false;
                }
            }

            try
            {
                var data = new
                {
                    Market = currenciesCopy.ToDictionary(c => c.Symbol, c => c),
                    Users = usersCopy.ToDictionary(u => u.UserId, u => u),
                    IsInitialized = true
                };
                IOFile.WriteAllText(JsonPath, JsonConvert.SerializeObject(data, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Naomi] Fatal save error: {ex.Message}");
            }
        }

        private static void SaveToSqliteSnapshot(List<Currency> currenciesCopy, List<User> usersCopy)
        {
            using var conn = GetDbConnection();
            using var trans = conn.BeginTransaction();

            using (var delCmd = conn.CreateCommand())
            {
                delCmd.Transaction = trans;
                delCmd.CommandText = "DELETE FROM Currencies";
                delCmd.ExecuteNonQuery();
            }

            foreach (var c in currenciesCopy)
            {
                using var insCmd = conn.CreateCommand();
                insCmd.Transaction = trans;
                insCmd.CommandText = @"
                    INSERT OR REPLACE INTO Currencies (Symbol, Description, BaseValue, TotalSupply, CirculatingSupply, PhotoUrl, PriceHistoryJson, CreatedAt, TimedPriceHistoryJson)
                    VALUES (@s, @d, @bv, @ts, @cs, @pu, @ph, @ca, @tph)";
                insCmd.Parameters.AddWithValue("@s", c.Symbol);
                insCmd.Parameters.AddWithValue("@d", c.Description ?? c.Symbol);
                insCmd.Parameters.AddWithValue("@bv", c.BaseValue);
                insCmd.Parameters.AddWithValue("@ts", c.TotalSupply);
                insCmd.Parameters.AddWithValue("@cs", c.CirculatingSupply);
                insCmd.Parameters.AddWithValue("@pu", c.PhotoUrl ?? "");
                insCmd.Parameters.AddWithValue("@ph", JsonConvert.SerializeObject(c.PriceHistory));
                insCmd.Parameters.AddWithValue("@ca", c.CreatedAt.ToString("o"));
                insCmd.Parameters.AddWithValue("@tph", JsonConvert.SerializeObject(c.TimedPriceHistory));
                insCmd.ExecuteNonQuery();
            }

            using (var delCmd = conn.CreateCommand())
            {
                delCmd.Transaction = trans;
                delCmd.CommandText = "DELETE FROM Orders";
                delCmd.ExecuteNonQuery();
            }

            foreach (var c in currenciesCopy)
            {
                foreach (var o in c.Orders)
                {
                    using var insCmd = conn.CreateCommand();
                    insCmd.Transaction = trans;
                    insCmd.CommandText = @"
                        INSERT INTO Orders (Symbol, UserId, Type, Price, Quantity, Timestamp)
                        VALUES (@sym, @u, @t, @p, @q, @ts)";
                    insCmd.Parameters.AddWithValue("@sym", c.Symbol);
                    insCmd.Parameters.AddWithValue("@u", o.UserId);
                    insCmd.Parameters.AddWithValue("@t", o.Type ?? "SELL");
                    insCmd.Parameters.AddWithValue("@p", o.Price);
                    insCmd.Parameters.AddWithValue("@q", o.Quantity);
                    insCmd.Parameters.AddWithValue("@ts", o.Timestamp.ToString("o"));
                    insCmd.ExecuteNonQuery();
                }
            }

            using (var delCmd = conn.CreateCommand())
            {
                delCmd.Transaction = trans;
                delCmd.CommandText = "DELETE FROM Users";
                delCmd.ExecuteNonQuery();
            }

            foreach (var u in usersCopy)
            {
                using var insCmd = conn.CreateCommand();
                insCmd.Transaction = trans;
                insCmd.CommandText = @"
                    INSERT OR REPLACE INTO Users (UserId, Username, Balance, Level, XP, TotalTrades, SuccessfulTrades, TotalProfit, CrisisSurvived, ReferralCode, Referrals, LastDailyReward, PortfolioJson, CostBasisJson, DeviceFingerprintsJson, HasUsedReferral, IsBanned)
                    VALUES (@uid, @un, @bal, @lvl, @xp, @tt, @st, @tp, @cs, @rc, @ref, @ldr, @port, @cb, @fp, @hur, @ban)";
                insCmd.Parameters.AddWithValue("@uid", u.UserId);
                insCmd.Parameters.AddWithValue("@un", u.Username ?? "unknown");
                insCmd.Parameters.AddWithValue("@bal", u.Balance);
                insCmd.Parameters.AddWithValue("@lvl", u.Level);
                insCmd.Parameters.AddWithValue("@xp", u.XP);
                insCmd.Parameters.AddWithValue("@tt", u.TotalTrades);
                insCmd.Parameters.AddWithValue("@st", u.SuccessfulTrades);
                insCmd.Parameters.AddWithValue("@tp", u.TotalProfit);
                insCmd.Parameters.AddWithValue("@cs", u.CrisisSurvived);
                insCmd.Parameters.AddWithValue("@rc", u.ReferralCode ?? ("REF" + u.UserId));
                insCmd.Parameters.AddWithValue("@ref", u.Referrals);
                insCmd.Parameters.AddWithValue("@ldr", u.LastDailyReward.ToString("o"));
                insCmd.Parameters.AddWithValue("@port", JsonConvert.SerializeObject(u.Portfolio));
                insCmd.Parameters.AddWithValue("@cb", JsonConvert.SerializeObject(u.CostBasis));
                insCmd.Parameters.AddWithValue("@fp", JsonConvert.SerializeObject(u.DeviceFingerprints));
                insCmd.Parameters.AddWithValue("@hur", u.HasUsedReferral ? 1 : 0);
                insCmd.Parameters.AddWithValue("@ban", u.IsBanned ? 1 : 0);
                insCmd.ExecuteNonQuery();
            }

            using (var initCmd = conn.CreateCommand())
            {
                initCmd.Transaction = trans;
                initCmd.CommandText = "INSERT OR REPLACE INTO Settings (Key, Value) VALUES ('IsInitialized', 'true')";
                initCmd.ExecuteNonQuery();
            }

            trans.Commit();
        }

        private static async Task BackgroundSaveWorker(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (_saveRequested)
                    {
                        _saveRequested = false;
                        SaveDataImmediate();
                    }
                }
                catch { }
                await Task.Delay(300, ct);
            }
        }

        public static async Task Main(string[] args)
        {
            try { SQLitePCL.Batteries.Init(); } catch { }

            LoadData();

            HttpClient httpClient;
            try
            {
                var handler = new SocketsHttpHandler
                {
                    PooledConnectionLifetime = TimeSpan.FromMinutes(15),
                    PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
                    KeepAlivePingDelay = TimeSpan.FromSeconds(20),
                    KeepAlivePingTimeout = TimeSpan.FromSeconds(10)
                };
                httpClient = new HttpClient(handler)
                {
                    Timeout = TimeSpan.FromSeconds(120)
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Naomi] SocketsHttpHandler fallback ({ex.Message}), using standard HttpClient.");
                httpClient = new HttpClient()
                {
                    Timeout = TimeSpan.FromSeconds(120)
                };
            }

            Bot = new TelegramBotClient(Token, httpClient);

            // حذف وب‌هوک قدیمی احتمالی برای اطمینان از عملکرد ۱۰۰٪ Polling
            try { await Bot.DeleteWebhook(cancellationToken: CancellationToken.None); } catch { }

            try
            {
                var me = await Bot.GetMe();
                BotUsername = me.Username ?? "NaomiBot";
                Console.WriteLine($"[Naomi] Bot started successfully: @{BotUsername} | Storage: {(_useSqlite ? DbPath : JsonPath)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Naomi] Note getting Bot info: {ex.Message}");
                BotUsername = "NaomiBot";
            }

            var cts = new CancellationTokenSource();
            AppDomain.CurrentDomain.ProcessExit += (s, e) => {
                SaveDataImmediate();
                cts.Cancel();
            };
            Console.CancelKeyPress += (s, e) => {
                e.Cancel = true;
                SaveDataImmediate();
                cts.Cancel();
            };

            _ = Task.Run(() => BackgroundSaveWorker(cts.Token), cts.Token);
            _ = Task.Run(DailyRewardScheduler, cts.Token);
            _ = Task.Run(DatabaseBackupScheduler, cts.Token);

            var receiverOptions = new ReceiverOptions { AllowedUpdates = Array.Empty<UpdateType>() };
            Bot.StartReceiving(HandleUpdateAsync, HandleErrorAsync, receiverOptions, cts.Token);

            Console.WriteLine("[Naomi] Ready and listening for Telegram updates! Press Ctrl+C to stop...");
            try { await Task.Delay(-1, cts.Token); } catch { }
            SaveDataImmediate();
        }

        private static void LoadData()
        {
            bool isInitialized = false;

            try
            {
                using var conn = GetDbConnection();
                using (var checkCmd = conn.CreateCommand())
                {
                    checkCmd.CommandText = "SELECT Value FROM Settings WHERE Key = 'IsInitialized'";
                    var val = checkCmd.ExecuteScalar()?.ToString();
                    isInitialized = (val == "true");
                }

                // مهاجرت خودکار از فایل قدیمی در صورت وجود
                if (!isInitialized && IOFile.Exists("stockbot_data.json"))
                {
                    try
                    {
                        var json = IOFile.ReadAllText("stockbot_data.json");
                        var data = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                        if (data != null)
                        {
                            if (data.ContainsKey("Market") && data["Market"] != null)
                            {
                                var m = JsonConvert.DeserializeObject<Dictionary<string, Currency>>(data["Market"].ToString() ?? "{}");
                                if (m != null) Market = m;
                            }
                            if (data.ContainsKey("Users") && data["Users"] != null)
                            {
                                var u = JsonConvert.DeserializeObject<Dictionary<long, User>>(data["Users"].ToString() ?? "{}");
                                if (u != null) Users = u;
                            }
                        }
                        try { IOFile.Move("stockbot_data.json", "stockbot_data.json.migrated"); } catch { }
                    }
                    catch { }
                }
                else
                {
                    LoadFromSqlite(conn);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Naomi] SQLite unavailable ({ex.Message}), switching to high-speed JSON persistence at {JsonPath}");
                _useSqlite = false;

                if (IOFile.Exists(JsonPath))
                {
                    try
                    {
                        var json = IOFile.ReadAllText(JsonPath);
                        var data = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                        if (data != null)
                        {
                            if (data.ContainsKey("Market") && data["Market"] != null)
                            {
                                var m = JsonConvert.DeserializeObject<Dictionary<string, Currency>>(data["Market"].ToString() ?? "{}");
                                if (m != null) Market = m;
                            }
                            if (data.ContainsKey("Users") && data["Users"] != null)
                            {
                                var u = JsonConvert.DeserializeObject<Dictionary<long, User>>(data["Users"].ToString() ?? "{}");
                                if (u != null) Users = u;
                            }
                            if (data.ContainsKey("IsInitialized"))
                            {
                                isInitialized = true;
                            }
                        }
                    }
                    catch { }
                }
                else if (IOFile.Exists("stockbot_data.json"))
                {
                    try
                    {
                        var json = IOFile.ReadAllText("stockbot_data.json");
                        var data = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                        if (data != null)
                        {
                            if (data.ContainsKey("Market") && data["Market"] != null)
                            {
                                var m = JsonConvert.DeserializeObject<Dictionary<string, Currency>>(data["Market"].ToString() ?? "{}");
                                if (m != null) Market = m;
                            }
                            if (data.ContainsKey("Users") && data["Users"] != null)
                            {
                                var u = JsonConvert.DeserializeObject<Dictionary<long, User>>(data["Users"].ToString() ?? "{}");
                                if (u != null) Users = u;
                            }
                        }
                        try { IOFile.Move("stockbot_data.json", "stockbot_data.json.migrated"); } catch { }
                    }
                    catch { }
                }
            }

            if (Market == null) Market = new();
            if (Users == null) Users = new();

            EnsureTreasuryAccount();

            // ارزهای پیش‌فرض فقط در اولین راه‌اندازی دیتابیس ساخته می‌شوند و در ریستارت‌های بعدی بازنمی‌گردند
            if (!isInitialized && Market.Count == 0)
            {
                EnsureDefaultCurrencies();
                SaveDataImmediate();
            }

            // پاک‌سازی دیتابیس از مقادیر null احتمالی
            foreach (var c in Market.Values)
            {
                if (c.Orders == null) c.Orders = new();
                if (c.PriceHistory == null) c.PriceHistory = new();
                if (c.PriceHistory.Count == 0) c.PriceHistory.Add(c.BaseValue > 0 ? c.BaseValue : 1m);
                if (c.Symbol == null) c.Symbol = "";
                if (c.Description == null) c.Description = c.Symbol;
                if (c.PhotoUrl == null) c.PhotoUrl = "";
                if (c.CreatedAt < DateTime.UtcNow.AddDays(-3)) c.CreatedAt = DateTime.UtcNow.AddHours(-12);
                if (c.TimedPriceHistory != null)
                {
                    foreach (var pt in c.TimedPriceHistory)
                    {
                        if (pt.Timestamp < DateTime.UtcNow.AddDays(-3)) pt.Timestamp = DateTime.UtcNow.AddHours(-12);
                    }
                }
            }

            foreach (var u in Users.Values)
            {
                if (u.Username == null) u.Username = "unknown";
                if (u.Portfolio == null) u.Portfolio = new();
                if (u.CostBasis == null) u.CostBasis = new();
                if (u.DeviceFingerprints == null) u.DeviceFingerprints = new();
                if (u.ReferralCode == null) u.ReferralCode = "REF" + u.UserId;
                foreach (var h in u.Portfolio.Where(x => x.Value > 0))
                {
                    if (!u.CostBasis.ContainsKey(h.Key))
                        u.CostBasis[h.Key] = GetCurrentPrice(h.Key);
                }
            }

            foreach (var symbol in Market.Keys.ToList())
            {
                EnsureInitialLiquidity(symbol);
                MatchOrders(symbol, 0);
            }
        }

        private static void LoadFromSqlite(SqliteConnection conn)
        {
            Market.Clear();
            Users.Clear();

            // 1. لود ارزها
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT Symbol, Description, BaseValue, TotalSupply, CirculatingSupply, PhotoUrl, PriceHistoryJson, CreatedAt, TimedPriceHistoryJson FROM Currencies";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var symbol = reader.GetString(0);
                    var c = new Currency
                    {
                        Symbol = symbol,
                        Description = reader.IsDBNull(1) ? symbol : reader.GetString(1),
                        BaseValue = reader.GetDecimal(2),
                        TotalSupply = reader.GetInt64(3),
                        CirculatingSupply = reader.GetInt64(4),
                        PhotoUrl = reader.IsDBNull(5) ? "" : reader.GetString(5)
                    };
                    var histJson = reader.IsDBNull(6) ? "[]" : reader.GetString(6);
                    c.PriceHistory = JsonConvert.DeserializeObject<List<decimal>>(histJson) ?? new List<decimal> { c.BaseValue };

                    if (reader.FieldCount > 7 && !reader.IsDBNull(7))
                    {
                        if (DateTime.TryParse(reader.GetString(7), out var ca))
                            c.CreatedAt = ca;
                    }
                    if (c.CreatedAt == DateTime.MinValue)
                        c.CreatedAt = DateTime.UtcNow.AddDays(-7);

                    if (reader.FieldCount > 8 && !reader.IsDBNull(8))
                    {
                        var tphJson = reader.GetString(8);
                        c.TimedPriceHistory = JsonConvert.DeserializeObject<List<PricePoint>>(tphJson) ?? new();
                    }

                    if (c.TimedPriceHistory == null || c.TimedPriceHistory.Count == 0)
                    {
                        c.TimedPriceHistory = new List<PricePoint>();
                        var startDt = c.CreatedAt;
                        for (int i = 0; i < c.PriceHistory.Count; i++)
                        {
                            var dt = startDt.AddHours(i * 4);
                            if (dt > DateTime.UtcNow) dt = DateTime.UtcNow;
                            c.TimedPriceHistory.Add(new PricePoint { Timestamp = dt, Price = c.PriceHistory[i] });
                        }
                    }

                    Market[symbol] = c;
                }
            }

            // 2. لود سفارشات
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT Symbol, UserId, Type, Price, Quantity, Timestamp FROM Orders";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var symbol = reader.GetString(0);
                    if (Market.TryGetValue(symbol, out var c))
                    {
                        c.Orders.Add(new Order
                        {
                            UserId = reader.GetInt64(1),
                            Type = reader.GetString(2),
                            Price = reader.GetDecimal(3),
                            Quantity = reader.GetInt64(4),
                            Timestamp = DateTime.TryParse(reader.GetString(5), out var dt) ? dt : DateTime.UtcNow
                        });
                    }
                }
            }

            // 3. لود کاربران
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT UserId, Username, Balance, Level, XP, TotalTrades, SuccessfulTrades, TotalProfit, CrisisSurvived, ReferralCode, Referrals, LastDailyReward, PortfolioJson, DeviceFingerprintsJson, CostBasisJson, HasUsedReferral, IsBanned FROM Users";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var userId = reader.GetInt64(0);
                    var u = new User
                    {
                        UserId = userId,
                        Username = reader.IsDBNull(1) ? "unknown" : reader.GetString(1),
                        Balance = reader.GetDecimal(2),
                        Level = reader.GetInt32(3),
                        XP = reader.GetInt32(4),
                        TotalTrades = reader.GetInt32(5),
                        SuccessfulTrades = reader.GetInt32(6),
                        TotalProfit = reader.GetDecimal(7),
                        CrisisSurvived = reader.GetInt32(8),
                        ReferralCode = reader.IsDBNull(9) ? ("REF" + userId) : reader.GetString(9),
                        Referrals = reader.GetInt32(10),
                        LastDailyReward = DateTime.TryParse(reader.IsDBNull(11) ? "" : reader.GetString(11), out var dr) ? dr : DateTime.MinValue
                    };
                    var portJson = reader.IsDBNull(12) ? "{}" : reader.GetString(12);
                    u.Portfolio = JsonConvert.DeserializeObject<Dictionary<string, long>>(portJson) ?? new();

                    var fpJson = reader.IsDBNull(13) ? "[]" : reader.GetString(13);
                    u.DeviceFingerprints = JsonConvert.DeserializeObject<List<long>>(fpJson) ?? new();

                    if (reader.FieldCount > 14 && !reader.IsDBNull(14))
                    {
                        var cbJson = reader.GetString(14);
                        u.CostBasis = JsonConvert.DeserializeObject<Dictionary<string, decimal>>(cbJson) ?? new();
                    }

                    if (reader.FieldCount > 15 && !reader.IsDBNull(15))
                    {
                        u.HasUsedReferral = reader.GetInt32(15) == 1;
                    }

                    if (reader.FieldCount > 16 && !reader.IsDBNull(16))
                    {
                        u.IsBanned = reader.GetInt32(16) == 1;
                    }

                    Users[userId] = u;
                }
            }
        }

        private static void EnsureTreasuryAccount()
        {
            if (!Users.ContainsKey(0))
            {
                Users[0] = new User
                {
                    UserId = 0,
                    Username = "Treasury (خزانه مرکزی)",
                    Balance = 1000000000m,
                    ReferralCode = "TREASURY00"
                };
            }
        }

        private static void EnsureDefaultCurrencies()
        {
            if (Market.Count == 0)
            {
                AddDefaultCurrency("BTC", "بیت‌کوین - پادشاه ارزهای دیجیتال", 65000m, 21000000, "https://assets.coingecko.com/coins/images/1/large/bitcoin.png");
                AddDefaultCurrency("ETH", "اتریوم - پلتفرم قراردادهای هوشمند", 3500m, 120000000, "https://assets.coingecko.com/coins/images/279/large/ethereum.png");
                AddDefaultCurrency("SOL", "سولانا - بلاکچین پرسرعت و مقیاس‌پذیر", 150m, 500000000, "https://assets.coingecko.com/coins/images/4128/large/solana.png");
                AddDefaultCurrency("TON", "تون‌کوین - ارز رسمی اکوسیستم تلگرام", 7.50m, 5000000000, "https://assets.coingecko.com/coins/images/17980/large/ton_symbol.png");
                AddDefaultCurrency("DOGE", "داج‌کوین - محبوب‌ترین میم‌کوین بازار", 0.15m, 140000000000, "https://assets.coingecko.com/coins/images/5/large/dogecoin.png");
            }

            foreach (var symbol in Market.Keys.ToList())
            {
                EnsureInitialLiquidity(symbol);
            }
        }

        private static void AddDefaultCurrency(string symbol, string desc, decimal baseValue, long totalSupply, string photoUrl)
        {
            if (!Market.ContainsKey(symbol))
            {
                var currency = new Currency
                {
                    Symbol = symbol,
                    Description = desc,
                    BaseValue = baseValue,
                    TotalSupply = totalSupply,
                    CirculatingSupply = totalSupply / 2,
                    PhotoUrl = photoUrl,
                    CreatedAt = DateTime.UtcNow.AddHours(-6)
                };
                currency.PriceHistory.Add(baseValue);
                currency.TimedPriceHistory.Add(new PricePoint { Timestamp = DateTime.UtcNow.AddHours(-6), Price = baseValue });
                currency.TimedPriceHistory.Add(new PricePoint { Timestamp = DateTime.UtcNow, Price = baseValue });
                Market[symbol] = currency;
            }
        }

        private static void EnsureInitialLiquidity(string symbol)
        {
            if (!Market.TryGetValue(symbol, out var currency)) return;

            decimal curPrice = GetCurrentPrice(symbol);
            if (curPrice <= 0) curPrice = currency.BaseValue > 0 ? currency.BaseValue : 1m;

            // حذف سفارشات خرید خزانه؛ فروش سهام توسط کاربران همیشه ۱۰۰٪ همتا به همتا (P2P) است
            currency.Orders.RemoveAll(o => o.Type == "BUY" && o.UserId == 0);

            // ایجاد سفارش فروش اولیه خزانه در قیمت پایه تنها تا زمانی که سهام اولیه دست مردم نیفتاده است
            var treasurySell = currency.Orders.FirstOrDefault(o => o.Type == "SELL" && o.UserId == 0 && o.Quantity > 0);
            if (treasurySell == null && currency.CirculatingSupply > 0)
            {
                currency.Orders.Add(new Order
                {
                    UserId = 0,
                    Type = "SELL",
                    Price = curPrice,
                    Quantity = currency.CirculatingSupply,
                    Timestamp = DateTime.UtcNow
                });
            }
            else if (treasurySell != null)
            {
                treasurySell.Price = curPrice;
                currency.CirculatingSupply = treasurySell.Quantity; // به‌روزرسانی سهام باقی‌مانده عرضه اولیه
            }

            if (!Users[0].Portfolio.ContainsKey(symbol) || Users[0].Portfolio[symbol] < currency.CirculatingSupply)
            {
                Users[0].Portfolio[symbol] = currency.TotalSupply;
            }
        }

        private static decimal GetCurrentPrice(string symbol)
        {
            if (Market.TryGetValue(symbol, out var c))
            {
                return c.PriceHistory.LastOrDefault(c.BaseValue);
            }
            return 1m;
        }

        private static async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
        {
            try
            {
                if (update.CallbackQuery is { } callbackQuery)
                {
                    await HandleCallbackQueryAsync(bot, callbackQuery, ct);
                    RequestSave();
                    return;
                }

                if (update.Message is not { } message || message.From is null) return;
                var chatId = message.Chat.Id;
                var userId = message.From.Id;
                var text = message.Text?.Trim() ?? "";

                User user;
                lock (_dataLock)
                {
                    if (!Users.TryGetValue(userId, out var u) || u == null)
                    {
                        u = new User
                        {
                            UserId = userId,
                            Username = message.From.Username ?? "unknown",
                            Balance = 5000m,
                            Level = 1,
                            XP = 0
                        };
                        u.DeviceFingerprints.Add(userId % 100000);
                        u.ReferralCode = "REF" + userId.ToString().Substring(Math.Max(0, userId.ToString().Length - 6));
                        Users[userId] = u;
                    }
                    else if (message.From.Username != null)
                    {
                        u.Username = message.From.Username;
                    }
                    user = u;
                }

                if (user.IsBanned && userId != OwnerId)
                {
                    await bot.SendMessage(chatId, "🚫 دسترسی حساب کاربری شما توسط مدیریت سرور مسدود شده است.", cancellationToken: ct);
                    return;
                }

                // مدیریت وضعیت‌های خاص (مانند ارسال عکس، متن یا وارد کردن مقدار دلخواه خرید و فروش)
                if (UserStates.TryGetValue(chatId, out var state))
                {
                    if (await HandleStateAsync(bot, message, state, ct))
                    {
                        RequestSave();
                        return;
                    }
                }

                if (userId == OwnerId && IsOwnerCommand(text))
                    await HandleOwnerCommands(bot, message, text, ct);
                else
                    await HandleUserCommands(bot, message, text, ct);

                RequestSave();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Update handling error: {ex.Message}");
            }
        }

        private static bool IsOwnerCommand(string text)
        {
            var ownerCmds = new[]
            {
                "➕ اضافه کردن ارز", "📊 مرور بازار", "💰 موجودی کاربران",
                "🏦 تزریق نقدینگی", "🖼 تنظیم عکس ارز", "📝 تنظیم توضیحات ارز",
                "🗑 حذف ارز", "📈 تنظیم قیمت دستی", "📋 سفارشات باز",
                "🔄 ریست بازار", "🎲 رویداد تصادفی", "📰 رویدادهای ویژه", "رویدادها",
                "خبر مثبت", "خبر منفی", "هک", "جنگ", "رکود", "رشد ناگهانی", "سقوط آزاد", "بازگشت",
                "🏆 لیدربورد", "🏆 لیدربورد برترین‌ها", "🔙 بازگشت به منوی اصلی", "پنل", "admin", "👑 پنل مدیریت",
                "🎁 واریز / مدیریت سهام", "مدیریت سهام", "🎮 کنترل پنل دارایی پلیر", "کنترل پلیر", "مدیریت پلیر"
            };
            return ownerCmds.Contains(text) || text.StartsWith("واریز سهام") || text.StartsWith("برداشت سهام") || text.StartsWith("واریز دلار") || text.StartsWith("برداشت دلار") || text.StartsWith("کنترل پلیر");
        }

        private static async Task<bool> HandleStateAsync(ITelegramBotClient bot, Message message, string state, CancellationToken ct)
        {
            var chatId = message.Chat.Id;
            var text = message.Text?.Trim() ?? "";

            if (text == "انصراف" || text == "لغو")
            {
                UserStates.Remove(chatId);
                await bot.SendMessage(chatId, "❌ عملیات لغو شد.", replyMarkup: GetOwnerKeyboard(), cancellationToken: ct);
                return true;
            }

            if (state == "ADD_SYMBOL")
            {
                var sym = text.ToUpper();
                if (string.IsNullOrWhiteSpace(sym)) return false;
                UserStates[chatId] = $"ADD_SUPPLY_{sym}";
                await bot.SendMessage(chatId, $"تعداد کل عرضه (Total Supply) برای ارز {sym} را وارد کنید:", cancellationToken: ct);
                return true;
            }
            else if (state.StartsWith("ADD_SUPPLY_"))
            {
                var symbol = state.Split('_')[2];
                if (long.TryParse(text, out var supply) && supply > 0)
                {
                    UserStates[chatId] = $"ADD_BASE_{symbol}_{supply}";
                    await bot.SendMessage(chatId, $"ارزش پایه هر واحد (به دلار $) را وارد کنید (مثال: 25.50):", cancellationToken: ct);
                    return true;
                }
            }
            else if (state.StartsWith("ADD_BASE_"))
            {
                var parts = state.Split('_');
                var symbol = parts[2];
                var supply = long.Parse(parts[3]);
                if (decimal.TryParse(text, out var baseVal) && baseVal > 0)
                {
                    UserStates[chatId] = $"ADD_PHOTO_{symbol}_{supply}_{baseVal}";
                    await bot.SendMessage(chatId, "آدرس تصویر (URL) یا FileId عکس آیکون ارز را ارسال کنید (یا تایپ کنید none):", cancellationToken: ct);
                    return true;
                }
            }
            else if (state.StartsWith("ADD_PHOTO_"))
            {
                var parts = state.Split('_');
                var symbol = parts[2];
                var supply = long.Parse(parts[3]);
                var baseVal = decimal.Parse(parts[4]);

                string photoUrl = "";
                if (message.Photo != null && message.Photo.Length > 0)
                    photoUrl = message.Photo.Last().FileId;
                else if (!string.IsNullOrWhiteSpace(text) && !text.Equals("none", StringComparison.OrdinalIgnoreCase))
                    photoUrl = text;

                UserStates[chatId] = $"ADD_DESC_{symbol}_{supply}_{baseVal}_{photoUrl}";
                await bot.SendMessage(chatId, "توضیحات کوتاه یا عنوان ارز را وارد کنید (یا تایپ کنید none):", cancellationToken: ct);
                return true;
            }
            else if (state.StartsWith("ADD_DESC_"))
            {
                var parts = state.Split('_');
                var symbol = parts[2];
                var supply = long.Parse(parts[3]);
                var baseVal = decimal.Parse(parts[4]);
                var photoUrl = parts[5];
                string desc = (text.Equals("none", StringComparison.OrdinalIgnoreCase)) ? symbol : text;

                lock (_dataLock)
                {
                    var c = new Currency
                    {
                        Symbol = symbol,
                        Description = desc,
                        TotalSupply = supply,
                        BaseValue = baseVal,
                        CirculatingSupply = supply / 2,
                        PhotoUrl = photoUrl
                    };
                    c.PriceHistory.Add(baseVal);
                    Market[symbol] = c;
                    EnsureInitialLiquidity(symbol);
                }

                UserStates.Remove(chatId);
                await bot.SendMessage(
                    chatId,
                    $"✅ ارز {symbol} ({desc}) با قیمت پایه {FmtPrice(baseVal)} و نقدینگی اولیه خزانه با موفقیت به بازار اضافه شد!",
                    replyMarkup: GetOwnerKeyboard(),
                    cancellationToken: ct
                );
                return true;
            }
            else if (state == "REMOVE_CURRENCY")
            {
                bool removed = false;
                lock (_dataLock)
                {
                    if (Market.ContainsKey(text.ToUpper()))
                    {
                        Market.Remove(text.ToUpper());
                        UserStates.Remove(chatId);
                        removed = true;
                    }
                }
                if (removed)
                {
                    await bot.SendMessage(chatId, "✅ ارز مورد نظر با موفقیت حذف شد.", replyMarkup: GetOwnerKeyboard(), cancellationToken: ct);
                    return true;
                }
            }
            else if (state == "SET_PRICE_SYMBOL")
            {
                var sym = text.ToUpper();
                if (Market.ContainsKey(sym))
                {
                    UserStates[chatId] = $"SET_PRICE_{sym}";
                    await bot.SendMessage(chatId, $"قیمت جدید دلار ($) برای ارز {sym} را وارد کنید:", cancellationToken: ct);
                    return true;
                }
            }
            else if (state.StartsWith("SET_PRICE_"))
            {
                var symbol = state.Split('_')[2];
                if (decimal.TryParse(text, out var newPrice))
                {
                    bool updated = false;
                    lock (_dataLock)
                    {
                        if (Market.ContainsKey(symbol))
                        {
                            Market[symbol].PriceHistory.Add(newPrice);
                            UserStates.Remove(chatId);
                            updated = true;
                        }
                    }
                    if (updated)
                    {
                        await bot.SendMessage(chatId, $"✅ قیمت ارز {symbol} به {FmtPrice(newPrice)} تغییر یافت.", replyMarkup: GetOwnerKeyboard(), cancellationToken: ct);
                        return true;
                    }
                }
            }
            else if (state == "SET_PHOTO_SYMBOL")
            {
                var sym = text.ToUpper();
                if (Market.ContainsKey(sym))
                {
                    UserStates[chatId] = $"SET_PHOTO_FILE_{sym}";
                    await bot.SendMessage(chatId, $"تصویر جدید را ارسال کنید (عکس، لینک URL یا FileId) برای {sym}:", cancellationToken: ct);
                    return true;
                }
            }
            else if (state.StartsWith("SET_PHOTO_FILE_"))
            {
                var symbol = state.Split('_')[3];
                string photoUrl = "";
                if (message.Photo != null && message.Photo.Length > 0)
                    photoUrl = message.Photo.Last().FileId;
                else if (!string.IsNullOrWhiteSpace(text))
                    photoUrl = text;

                bool photoSet = false;
                lock (_dataLock)
                {
                    if (Market.ContainsKey(symbol) && !string.IsNullOrWhiteSpace(photoUrl))
                    {
                        Market[symbol].PhotoUrl = photoUrl;
                        UserStates.Remove(chatId);
                        photoSet = true;
                    }
                }
                if (photoSet)
                {
                    await bot.SendMessage(chatId, $"✅ تصویر/آیکون ارز {symbol} با موفقیت تنظیم شد!", replyMarkup: GetOwnerKeyboard(), cancellationToken: ct);
                    return true;
                }
            }
            else if (state == "SET_DESC_SYMBOL")
            {
                var sym = text.ToUpper();
                if (Market.ContainsKey(sym))
                {
                    UserStates[chatId] = $"SET_DESC_TEXT_{sym}";
                    await bot.SendMessage(chatId, $"توضیحات جدید را برای ارز {sym} وارد کنید:", cancellationToken: ct);
                    return true;
                }
            }
            else if (state.StartsWith("SET_DESC_TEXT_"))
            {
                var symbol = state.Split('_')[3];
                bool descSet = false;
                lock (_dataLock)
                {
                    if (Market.ContainsKey(symbol))
                    {
                        Market[symbol].Description = text;
                        UserStates.Remove(chatId);
                        descSet = true;
                    }
                }
                if (descSet)
                {
                    await bot.SendMessage(chatId, $"✅ توضیحات ارز {symbol} با موفقیت به‌روزرسانی شد!", replyMarkup: GetOwnerKeyboard(), cancellationToken: ct);
                    return true;
                }
            }
            else if (state == "ADM_CTRL_SELECT_USER" || state == "ADM_RESET_USER")
            {
                var target = FindUserByIdOrUsernameOrRank(text, out _);
                if (target != null)
                {
                    await SendPlayerGodModeDashboardAsync(bot, chatId, target, ct);
                }
                else
                {
                    await bot.SendMessage(chatId, "❌ پلیر مورد نظر یافت نشد.", replyMarkup: GetOwnerKeyboard(), cancellationToken: ct);
                }
                UserStates.Remove(chatId);
                return true;
            }
            else if (state.StartsWith("ADM_CTRL_ADD_CASH_AMT_"))
            {
                var targetId = long.Parse(state.Split('_')[5]);
                if (decimal.TryParse(text, out var amount) && amount > 0)
                {
                    User? target = null;
                    lock (_dataLock)
                    {
                        if (Users.TryGetValue(targetId, out var u))
                        {
                            u.Balance += amount;
                            target = u;
                        }
                    }
                    if (target != null)
                    {
                        RequestSave();
                        await bot.SendMessage(chatId, $"✅ مبلغ {FmtMoney(amount)} دلار با موفقیت به موجودی کاربر @{target.Username} اضافه شد!", replyMarkup: GetOwnerKeyboard(), cancellationToken: ct);
                        try { _ = Bot.SendMessage(target.UserId, $"🎁 **اعلان مدیریت:** مبلغ {FmtMoney(amount)} از طرف مدیریت به موجودی حساب شما واریز شد."); } catch { }
                        await SendPlayerGodModeDashboardAsync(bot, chatId, target, ct);
                    }
                    UserStates.Remove(chatId);
                    return true;
                }
            }
            else if (state.StartsWith("ADM_CTRL_SUB_CASH_AMT_"))
            {
                var targetId = long.Parse(state.Split('_')[5]);
                if (decimal.TryParse(text, out var amount) && amount > 0)
                {
                    User? target = null;
                    lock (_dataLock)
                    {
                        if (Users.TryGetValue(targetId, out var u))
                        {
                            u.Balance = Math.Max(0m, u.Balance - amount);
                            target = u;
                        }
                    }
                    if (target != null)
                    {
                        RequestSave();
                        await bot.SendMessage(chatId, $"✅ مبلغ {FmtMoney(amount)} دلار با موفقیت از موجودی کاربر @{target.Username} کسر شد!", replyMarkup: GetOwnerKeyboard(), cancellationToken: ct);
                        await SendPlayerGodModeDashboardAsync(bot, chatId, target, ct);
                    }
                    UserStates.Remove(chatId);
                    return true;
                }
            }
            else if (state.StartsWith("ADM_CTRL_ADD_STOCK_QTY_"))
            {
                var parts = state.Split('_');
                var targetId = long.Parse(parts[5]);
                var symbol = parts[6];
                if (long.TryParse(text, out var qty) && qty > 0)
                {
                    User? target = null;
                    lock (_dataLock)
                    {
                        if (Users.TryGetValue(targetId, out var u) && Market.ContainsKey(symbol))
                        {
                            if (!u.Portfolio.ContainsKey(symbol)) u.Portfolio[symbol] = 0;
                            u.Portfolio[symbol] += qty;
                            target = u;
                        }
                    }
                    if (target != null)
                    {
                        RequestSave();
                        await bot.SendMessage(chatId, $"✅ تعداد {qty:N0} واحد سهام {symbol} با موفقیت به سبد کاربر @{target.Username} واریز شد!", replyMarkup: GetOwnerKeyboard(), cancellationToken: ct);
                        try { _ = Bot.SendMessage(target.UserId, $"🎁 **اعلان مدیریت:** تعداد {qty:N0} واحد سهام {symbol} به سبد دارایی شما اضافه شد."); } catch { }
                        await SendPlayerGodModeDashboardAsync(bot, chatId, target, ct);
                    }
                    UserStates.Remove(chatId);
                    return true;
                }
            }
            else if (state.StartsWith("ADM_CTRL_SUB_STOCK_QTY_"))
            {
                var parts = state.Split('_');
                var targetId = long.Parse(parts[5]);
                var symbol = parts[6];
                if (long.TryParse(text, out var qty) && qty > 0)
                {
                    User? target = null;
                    lock (_dataLock)
                    {
                        if (Users.TryGetValue(targetId, out var u))
                        {
                            long cur = u.Portfolio.TryGetValue(symbol, out var sq) ? sq : 0;
                            if (cur <= qty) u.Portfolio.Remove(symbol);
                            else u.Portfolio[symbol] = cur - qty;
                            target = u;
                        }
                    }
                    if (target != null)
                    {
                        RequestSave();
                        await bot.SendMessage(chatId, $"✅ تعداد {qty:N0} واحد سهام {symbol} از سبد کاربر @{target.Username} کسر شد!", replyMarkup: GetOwnerKeyboard(), cancellationToken: ct);
                        await SendPlayerGodModeDashboardAsync(bot, chatId, target, ct);
                    }
                    UserStates.Remove(chatId);
                    return true;
                }
            }
            else if (state.StartsWith("ADM_CTRL_ADD_CASH_AMT_"))
            {
                var targetId = long.Parse(state.Split('_')[5]);
                if (decimal.TryParse(text, out var amount) && amount > 0)
                {
                    User? target = null;
                    lock (_dataLock)
                    {
                        if (Users.TryGetValue(targetId, out var u))
                        {
                            u.Balance += amount;
                            target = u;
                        }
                    }
                    if (target != null)
                    {
                        RequestSave();
                        await bot.SendMessage(chatId, $"✅ مبلغ {FmtMoney(amount)} دلار با موفقیت به موجودی کاربر @{target.Username} اضافه شد!", replyMarkup: GetOwnerKeyboard(), cancellationToken: ct);
                        try { _ = Bot.SendMessage(target.UserId, $"🎁 **اعلان مدیریت:** مبلغ {FmtMoney(amount)} از طرف مدیریت به موجودی حساب شما واریز شد."); } catch { }
                        await SendPlayerGodModeDashboardAsync(bot, chatId, target, ct);
                    }
                    UserStates.Remove(chatId);
                    return true;
                }
            }
            else if (state.StartsWith("ADM_CTRL_SUB_CASH_AMT_"))
            {
                var targetId = long.Parse(state.Split('_')[5]);
                if (decimal.TryParse(text, out var amount) && amount > 0)
                {
                    User? target = null;
                    lock (_dataLock)
                    {
                        if (Users.TryGetValue(targetId, out var u))
                        {
                            u.Balance = Math.Max(0m, u.Balance - amount);
                            target = u;
                        }
                    }
                    if (target != null)
                    {
                        RequestSave();
                        await bot.SendMessage(chatId, $"✅ مبلغ {FmtMoney(amount)} دلار با موفقیت از موجودی کاربر @{target.Username} کسر شد!", replyMarkup: GetOwnerKeyboard(), cancellationToken: ct);
                        await SendPlayerGodModeDashboardAsync(bot, chatId, target, ct);
                    }
                    UserStates.Remove(chatId);
                    return true;
                }
            }
            else if (state.StartsWith("ADM_CTRL_ADD_STOCK_QTY_"))
            {
                var parts = state.Split('_');
                var targetId = long.Parse(parts[5]);
                var symbol = parts[6];
                if (long.TryParse(text, out var qty) && qty > 0)
                {
                    User? target = null;
                    lock (_dataLock)
                    {
                        if (Users.TryGetValue(targetId, out var u) && Market.ContainsKey(symbol))
                        {
                            if (!u.Portfolio.ContainsKey(symbol)) u.Portfolio[symbol] = 0;
                            u.Portfolio[symbol] += qty;
                            target = u;
                        }
                    }
                    if (target != null)
                    {
                        RequestSave();
                        await bot.SendMessage(chatId, $"✅ تعداد {qty:N0} واحد سهام {symbol} با موفقیت به سبد کاربر @{target.Username} واریز شد!", replyMarkup: GetOwnerKeyboard(), cancellationToken: ct);
                        try { _ = Bot.SendMessage(target.UserId, $"🎁 **اعلان مدیریت:** تعداد {qty:N0} واحد سهام {symbol} به سبد دارایی شما اضافه شد."); } catch { }
                        await SendPlayerGodModeDashboardAsync(bot, chatId, target, ct);
                    }
                    UserStates.Remove(chatId);
                    return true;
                }
            }
            else if (state.StartsWith("ADM_CTRL_SUB_STOCK_QTY_"))
            {
                var parts = state.Split('_');
                var targetId = long.Parse(parts[5]);
                var symbol = parts[6];
                if (long.TryParse(text, out var qty) && qty > 0)
                {
                    User? target = null;
                    lock (_dataLock)
                    {
                        if (Users.TryGetValue(targetId, out var u))
                        {
                            long cur = u.Portfolio.TryGetValue(symbol, out var sq) ? sq : 0;
                            if (cur <= qty) u.Portfolio.Remove(symbol);
                            else u.Portfolio[symbol] = cur - qty;
                            target = u;
                        }
                    }
                    if (target != null)
                    {
                        RequestSave();
                        await bot.SendMessage(chatId, $"✅ تعداد {qty:N0} واحد سهام {symbol} از سبد کاربر @{target.Username} کسر شد!", replyMarkup: GetOwnerKeyboard(), cancellationToken: ct);
                        await SendPlayerGodModeDashboardAsync(bot, chatId, target, ct);
                    }
                    UserStates.Remove(chatId);
                    return true;
                }
            }
            else if (state.StartsWith("CUSTOM_BUY_QTY_"))
            {
                var symbol = state.Split('_')[3];
                if (long.TryParse(text, out var qty) && qty > 0)
                {
                    User user;
                    decimal price;
                    bool canBuy = false;
                    decimal totalCost = 0m;
                    lock (_dataLock)
                    {
                        if (Market.TryGetValue(symbol, out var c))
                        {
                            user = Users[message.From!.Id];
                            price = GetCurrentPrice(symbol);
                            totalCost = price * qty * 1.01m;
                            if (user.Balance >= totalCost)
                            {
                                canBuy = true;
                                var order = new Order { UserId = user.UserId, Type = "BUY", Price = price, Quantity = qty, Timestamp = DateTime.UtcNow };
                                c.Orders.Add(order);
                                MatchOrders(symbol, user.UserId);
                            }
                        }
                        else
                        {
                            return false;
                        }
                    }

                    if (!canBuy)
                    {
                        await bot.SendMessage(chatId, $"❌ موجودی نقدی دلار شما کافی نیست.\nمبلغ مورد نیاز با کارمزد: {FmtMoney(totalCost)}", cancellationToken: ct);
                    }
                    else
                    {
                        RequestSave();
                        var u = Users[message.From!.Id];
                        await bot.SendMessage(
                            chatId,
                            $"✅ خرید فوری {qty:N0} واحد {symbol} به قیمت واحد {FmtPrice(GetCurrentPrice(symbol))} با موفقیت انجام شد!\n💰 موجودی جدید دلار شما: {FmtMoney(u.Balance)}",
                            replyMarkup: u.UserId == OwnerId ? GetOwnerKeyboard() : GetUserKeyboard(u.UserId),
                            cancellationToken: ct
                        );
                    }
                    UserStates.Remove(chatId);
                    return true;
                }
                else
                {
                    await bot.SendMessage(chatId, "❌ لطفاً یک عدد معتبر بزرگتر از ۰ وارد کنید (یا تایپ کنید لغو):", cancellationToken: ct);
                    return true;
                }
            }
            else if (state.StartsWith("CUSTOM_SELL_QTY_"))
            {
                var symbol = state.Split('_')[3];
                if (long.TryParse(text, out var qty) && qty > 0)
                {
                    bool canSell = false;
                    long hasStock = 0;
                    decimal price = 0m;
                    lock (_dataLock)
                    {
                        if (Market.TryGetValue(symbol, out var c))
                        {
                            var user = Users[message.From!.Id];
                            hasStock = user.Portfolio.TryGetValue(symbol, out var sq) ? sq : 0;
                            if (hasStock >= qty)
                            {
                                canSell = true;
                                price = Math.Max(0.01m, GetCurrentPrice(symbol) * 0.95m);
                                var order = new Order { UserId = user.UserId, Type = "SELL", Price = price, Quantity = qty, Timestamp = DateTime.UtcNow };
                                c.Orders.Add(order);
                                MatchOrders(symbol, user.UserId);
                            }
                        }
                        else
                        {
                            return false;
                        }
                    }

                    if (!canSell)
                    {
                        await bot.SendMessage(chatId, $"❌ موجودی سهام {symbol} شما کافی نیست. موجودی شما: {hasStock:N0} واحد", cancellationToken: ct);
                    }
                    else
                    {
                        RequestSave();
                        var u = Users[message.From!.Id];
                        await bot.SendMessage(
                            chatId,
                            $"✅ فروش فوری {qty:N0} واحد {symbol} به قیمت واحد {FmtPrice(price)} با موفقیت انجام شد!\n💰 موجودی جدید دلار شما: {FmtMoney(u.Balance)}",
                            replyMarkup: u.UserId == OwnerId ? GetOwnerKeyboard() : GetUserKeyboard(u.UserId),
                            cancellationToken: ct
                        );
                    }
                    UserStates.Remove(chatId);
                    return true;
                }
                else
                {
                    await bot.SendMessage(chatId, "❌ لطفاً یک عدد معتبر بزرگتر از ۰ وارد کنید (یا تایپ کنید لغو):", cancellationToken: ct);
                    return true;
                }
            }
            else if (state.StartsWith("LIMIT_BUY_QTY_"))
            {
                var symbol = state.Split('_')[3];
                if (long.TryParse(text, out var qty) && qty > 0)
                {
                    UserStates[chatId] = $"LIMIT_BUY_PRICE_{symbol}_{qty}";
                    await bot.SendMessage(chatId, $"💵 قیمت پیشنهادی شما برای خرید هر ۱ واحد {symbol} چند دلار ($) است؟ (مثال: 80):", cancellationToken: ct);
                    return true;
                }
                else
                {
                    await bot.SendMessage(chatId, "❌ لطفاً یک عدد معتبر بزرگتر از ۰ وارد کنید (یا تایپ کنید لغو):", cancellationToken: ct);
                    return true;
                }
            }
            else if (state.StartsWith("LIMIT_BUY_PRICE_"))
            {
                var parts = state.Split('_');
                var symbol = parts[3];
                var qty = long.Parse(parts[4]);
                if (decimal.TryParse(text, out var price) && price > 0)
                {
                    bool placed = false;
                    decimal totalCost = price * qty * 1.01m;
                    lock (_dataLock)
                    {
                        if (Market.TryGetValue(symbol, out var c))
                        {
                            var user = Users[message.From!.Id];
                            if (GetAvailableCash(user) >= totalCost)
                            {
                                c.Orders.Add(new Order { UserId = user.UserId, Type = "BUY", Price = price, Quantity = qty, Timestamp = DateTime.UtcNow });
                                MatchOrders(symbol, user.UserId);
                                placed = true;
                            }
                        }
                    }
                    if (placed)
                    {
                        RequestSave();
                        var u = Users[message.From!.Id];
                        await bot.SendMessage(
                            chatId,
                            $"✅ سفارش خرید {qty:N0} واحد {symbol} به قیمت هر واحد {FmtPrice(price)} (ارزش کل: {FmtMoney(price * qty)}) ثبت شد!\nبه محض اینکه فروشنده‌ای در این قیمت پیدا شود، خرید شما به طور خودکار انجام خواهد شد.",
                            replyMarkup: u.UserId == OwnerId ? GetOwnerKeyboard() : GetUserKeyboard(u.UserId),
                            cancellationToken: ct
                        );
                    }
                    else
                    {
                        await bot.SendMessage(chatId, $"❌ موجودی نقدی دلار شما برای این سفارش کافی نیست.\nمبلغ مورد نیاز: {FmtMoney(totalCost)}", cancellationToken: ct);
                    }
                    UserStates.Remove(chatId);
                    return true;
                }
                else
                {
                    await bot.SendMessage(chatId, "❌ لطفاً یک قیمت معتبر بزرگتر از ۰ وارد کنید:", cancellationToken: ct);
                    return true;
                }
            }
            else if (state.StartsWith("LIMIT_SELL_QTY_"))
            {
                var symbol = state.Split('_')[3];
                if (long.TryParse(text, out var qty) && qty > 0)
                {
                    UserStates[chatId] = $"LIMIT_SELL_PRICE_{symbol}_{qty}";
                    await bot.SendMessage(chatId, $"💵 قیمت پیشنهادی شما برای فروش هر ۱ واحد {symbol} چند دلار ($) است؟ (مثال: 95):", cancellationToken: ct);
                    return true;
                }
                else
                {
                    await bot.SendMessage(chatId, "❌ لطفاً یک عدد معتبر بزرگتر از ۰ وارد کنید (یا تایپ کنید لغو):", cancellationToken: ct);
                    return true;
                }
            }
            else if (state.StartsWith("LIMIT_SELL_PRICE_"))
            {
                var parts = state.Split('_');
                var symbol = parts[3];
                var qty = long.Parse(parts[4]);
                if (decimal.TryParse(text, out var price) && price > 0)
                {
                    bool placed = false;
                    long hasStock = 0;
                    lock (_dataLock)
                    {
                        if (Market.TryGetValue(symbol, out var c))
                        {
                            var user = Users[message.From!.Id];
                            hasStock = GetAvailableStock(user, symbol);
                            if (hasStock >= qty)
                            {
                                c.Orders.Add(new Order { UserId = user.UserId, Type = "SELL", Price = price, Quantity = qty, Timestamp = DateTime.UtcNow });
                                MatchOrders(symbol, user.UserId);
                                placed = true;
                            }
                        }
                    }
                    if (placed)
                    {
                        RequestSave();
                        var u = Users[message.From!.Id];
                        await bot.SendMessage(
                            chatId,
                            $"✅ سفارش فروش {qty:N0} واحد {symbol} به قیمت هر واحد {FmtPrice(price)} (ارزش کل: {FmtMoney(price * qty)}) ثبت شد!\nبه محض اینکه خریداری در این قیمت پیدا شود، سهام شما به طور خودکار فروخته می‌شود.",
                            replyMarkup: u.UserId == OwnerId ? GetOwnerKeyboard() : GetUserKeyboard(u.UserId),
                            cancellationToken: ct
                        );
                    }
                    else
                    {
                        await bot.SendMessage(chatId, $"❌ موجودی سهام {symbol} شما کافی نیست. موجودی: {hasStock:N0} واحد", cancellationToken: ct);
                    }
                    UserStates.Remove(chatId);
                    return true;
                }
                else
                {
                    await bot.SendMessage(chatId, "❌ لطفاً یک قیمت معتبر بزرگتر از ۰ وارد کنید:", cancellationToken: ct);
                    return true;
                }
            }

            return false;
        }

        private static User? FindUserByIdOrUsernameOrRank(string input, out int rank)
        {
            rank = 0;
            input = input.Trim();
            if (string.IsNullOrWhiteSpace(input)) return null;

            string rankStr = input.TrimStart('#').Replace("رتبه", "").Replace("rank", "").Trim();
            if (int.TryParse(rankStr, out var r) && r >= 1 && r <= 100)
            {
                List<User> leaderboard;
                lock (_dataLock)
                {
                    leaderboard = Users.Values
                        .Where(u => u.UserId != 0)
                        .OrderByDescending(u => u.Balance + u.Portfolio.Sum(p => p.Value * GetCurrentPrice(p.Key)))
                        .ToList();
                }
                if (r <= leaderboard.Count)
                {
                    rank = r;
                    return leaderboard[r - 1];
                }
            }

            string idStr = input.TrimStart('@');
            if (long.TryParse(idStr, out var uid))
            {
                lock (_dataLock)
                {
                    if (Users.TryGetValue(uid, out var userById))
                    {
                        var lb = Users.Values
                            .Where(u => u.UserId != 0)
                            .OrderByDescending(u => u.Balance + u.Portfolio.Sum(p => p.Value * GetCurrentPrice(p.Key)))
                            .ToList();
                        int idx = lb.FindIndex(u => u.UserId == uid);
                        if (idx >= 0) rank = idx + 1;
                        return userById;
                    }
                }
            }

            lock (_dataLock)
            {
                var userByUn = Users.Values.FirstOrDefault(u => string.Equals(u.Username, idStr, StringComparison.OrdinalIgnoreCase));
                if (userByUn != null)
                {
                    var lb = Users.Values
                        .Where(u => u.UserId != 0)
                        .OrderByDescending(u => u.Balance + u.Portfolio.Sum(p => p.Value * GetCurrentPrice(p.Key)))
                        .ToList();
                    int idx = lb.FindIndex(u => u.UserId == userByUn.UserId);
                    if (idx >= 0) rank = idx + 1;
                    return userByUn;
                }
            }

            return null;
        }

        private static User? FindUserByIdOrUsername(string input)
        {
            return FindUserByIdOrUsernameOrRank(input, out _);
        }

        private static async Task SendPlayerGodModeDashboardAsync(ITelegramBotClient bot, long chatId, User target, CancellationToken ct)
        {
            int rank = 0;
            decimal netWorth = 0m;
            decimal totalStockVal = 0m;

            lock (_dataLock)
            {
                var leaderboard = Users.Values
                    .Where(u => u.UserId != 0)
                    .OrderByDescending(u => u.Balance + u.Portfolio.Sum(p => p.Value * GetCurrentPrice(p.Key)))
                    .ToList();
                int idx = leaderboard.FindIndex(u => u.UserId == target.UserId);
                if (idx >= 0) rank = idx + 1;

                foreach (var p in target.Portfolio)
                {
                    totalStockVal += p.Value * GetCurrentPrice(p.Key);
                }
                netWorth = target.Balance + totalStockVal;
            }

            string rankStr = rank > 0 ? $"#{rank}" : "نامشخص";
            string banStr = target.IsBanned ? "🚫 مسدود (Banned)" : "🟢 فعال (Active)";
            string refStr = target.HasUsedReferral ? "استفاده‌شده" : "آزاد";

            string msg = $"👑 **کنترل پنل پیشرفته دارایی پلیر (God-Mode Dashboard)**\n" +
                         $"━━━ @{target.Username} ━━━\n\n" +
                         $"🆔 آیدی عددی: `{target.UserId}` | 🏆 رتبه لیدربورد: **{rankStr}**\n" +
                         $"🛡 وضعیت حساب: **{banStr}**\n\n" +
                         $"💵 موجودی نقدی دلار (Cash): **{FmtMoney(target.Balance)}**\n" +
                         $"💎 ارزش کل سهام‌ها (Stock Value): **{FmtMoney(totalStockVal)}**\n" +
                         $"🏆 ارزش کل دارایی حساب (Net Worth): **{FmtMoney(netWorth)}**\n\n" +
                         $"📈 سطح کاربری: Level {target.Level} (XP: {target.XP})\n" +
                         $"🔄 تعداد معاملات: {target.TotalTrades} (سود کل محقق‌شده: {FmtMoney(target.TotalProfit)})\n" +
                         $"🎁 وضعیت کد دعوت: {refStr} (تعداد دعوت‌ها: {target.Referrals} نفر)\n\n" +
                         $"━━━━━━━━━━━━━━━━━━━━━━\n" +
                         $"📦 **سبد سهام فعلی کاربر:**\n";

            if (target.Portfolio.Count == 0 || target.Portfolio.All(p => p.Value <= 0))
            {
                msg += "• *(سبد سهام خالی است)*\n";
            }
            else
            {
                lock (_dataLock)
                {
                    foreach (var p in target.Portfolio.Where(x => x.Value > 0))
                    {
                        decimal curPrice = GetCurrentPrice(p.Key);
                        msg += $"🔸 **{p.Key}:** {p.Value:N0} واحد (ارزش: {FmtMoney(p.Value * curPrice)})\n";
                    }
                }
            }

            msg += $"━━━━━━━━━━━━━━━━━━━━━━\n" +
                   $"💡 **عملیات دستکاری مورد نظر را انتخاب کنید:**";

            var kb = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("💵 واریز دلار (+)", $"ADM_CTRL_ADD_CASH_{target.UserId}"),
                    InlineKeyboardButton.WithCallbackData("💸 کسر دلار (-)", $"ADM_CTRL_SUB_CASH_{target.UserId}")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("📦 واریز سهام (Stock +)", $"ADM_CTRL_ADD_STOCK_{target.UserId}"),
                    InlineKeyboardButton.WithCallbackData("🗑 کسر سهام (Stock -)", $"ADM_CTRL_SUB_STOCK_{target.UserId}")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("🔄 ریست کامل حساب (Reset)", $"ADM_CTRL_RESET_{target.UserId}"),
                    InlineKeyboardButton.WithCallbackData("🧹 حذف تمام سهام‌ها (Clear)", $"ADM_CTRL_CLEAR_STOCK_{target.UserId}")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData(target.IsBanned ? "🟢 آزادسازی حساب (Unban)" : "🚫 مسدودسازی (Ban)", $"ADM_CTRL_BAN_{target.UserId}"),
                    InlineKeyboardButton.WithCallbackData("⚡ اهدای لول/XP (Boost)", $"ADM_CTRL_BOOST_{target.UserId}")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("🔄 به‌روزرسانی پنل پلیر", $"ADM_CTRL_REFRESH_{target.UserId}")
                }
            });

            await bot.SendMessage(chatId, msg, parseMode: ParseMode.Markdown, replyMarkup: kb, cancellationToken: ct);
        }

        private static User? FindUserByIdOrUsername(string input)
        {
            input = input.Trim().TrimStart('@');
            if (long.TryParse(input, out var uid) && Users.TryGetValue(uid, out var userById))
            {
                return userById;
            }
            return Users.Values.FirstOrDefault(u => string.Equals(u.Username, input, StringComparison.OrdinalIgnoreCase));
        }

        // ===================== OWNER PANEL =====================
        private static async Task HandleOwnerCommands(ITelegramBotClient bot, Message message, string text, CancellationToken ct)
        {
            var chatId = message.Chat.Id;

            if (text == "پنل" || text == "admin" || text == "👑 پنل مدیریت")
            {
                await bot.SendMessage(
                    chatId,
                    "👑 به پنل مدیریت پیشرفته نائومی (Naomi) خوش آمدید!",
                    replyMarkup: GetOwnerKeyboard(),
                    cancellationToken: ct
                );
                return;
            }

            if (text == "➕ اضافه کردن ارز")
            {
                UserStates[chatId] = "ADD_SYMBOL";
                await bot.SendMessage(chatId, "نماد ارز جدید را وارد کنید (مثال: XRP):", cancellationToken: ct);
            }
            else if (text == "📊 مرور بازار")
            {
                string msg = "👑 وضعیت کامل بازار در یک نگاه:\n\n";
                lock (_dataLock)
                {
                    foreach (var c in Market.Values)
                    {
                        decimal price = GetCurrentPrice(c.Symbol);
                        int sells = c.Orders.Count(o => o.Type == "SELL");
                        int buys = c.Orders.Count(o => o.Type == "BUY");
                        msg += $"🔸 {c.Symbol} | قیمت لحظه‌ای: {FmtPrice(price)} | قیمت پایه: {FmtPrice(c.BaseValue)}\n" +
                               $"   عرضه در گردش: {c.CirculatingSupply:N0} / {c.TotalSupply:N0}\n" +
                               $"   سفارشات باز: {sells} فروش | {buys} خرید\n\n";
                    }
                }
                await bot.SendMessage(chatId, msg, replyMarkup: GetOwnerKeyboard(), cancellationToken: ct);
            }
            else if (text == "💰 موجودی کاربران")
            {
                string msg;
                lock (_dataLock)
                {
                    int totalUsers = Users.Count(u => u.Key != 0);
                    decimal totalCash = Users.Values.Where(u => u.UserId != 0).Sum(u => u.Balance);
                    decimal totalStockVal = 0m;
                    foreach (var u in Users.Values.Where(u => u.UserId != 0))
                    {
                        foreach (var p in u.Portfolio)
                        {
                            totalStockVal += p.Value * GetCurrentPrice(p.Key);
                        }
                    }

                    msg = $"👑 آمار کلی دارایی و موجودی کاربران:\n\n" +
                          $"👥 تعداد کل معامله‌گران: {totalUsers:N0} نفر\n" +
                          $"💵 مجموع دلار نقدی کاربران: {FmtMoney(totalCash)}\n" +
                          $"📦 مجموع ارزش سهام دست مردم: {FmtMoney(totalStockVal)}\n" +
                          $"💎 مجموع کل ارزش بازار دست مردم: {FmtMoney(totalCash + totalStockVal)}\n\n" +
                          $"🔝 ۵ معامله‌گر برتر:\n";

                    var top5 = Users.Values
                        .Where(u => u.UserId != 0)
                        .OrderByDescending(u => u.Balance + u.Portfolio.Sum(p => p.Value * GetCurrentPrice(p.Key)))
                        .Take(5)
                        .ToList();

                    for (int i = 0; i < top5.Count; i++)
                    {
                        var u = top5[i];
                        decimal net = u.Balance + u.Portfolio.Sum(p => p.Value * GetCurrentPrice(p.Key));
                        msg += $"{i + 1}. @{u.Username} — نقد: {FmtMoney(u.Balance)} | ارزش کل: {FmtMoney(net)}\n";
                    }
                }

                await bot.SendMessage(chatId, msg, replyMarkup: GetOwnerKeyboard(), cancellationToken: ct);
            }
            else if (text == "🎁 واریز / مدیریت سهام" || text == "مدیریت سهام" || text == "واریز سهام")
            {
                UserStates[chatId] = "ADM_SELECT_USER";
                string info = "👑 **مدیریت دارایی و سهام کاربران:**\n\n" +
                              "لطفاً آیدی عددی (UserId) یا یوزرنیم تلگرام (با @ یا بدون @) کاربری که می‌خواهید سهام یا دلار برایش واریز/کسر کنید را ارسال کنید.\n" +
                              "*(برای مدیریت حساب مدیریت خودتان، آیدی 8248899977 را بفرستید)*\n\n" +
                              "💡 همچنین می‌توانید از دستورات متنی سریع زیر استفاده کنید:\n" +
                              "• `واریز سهام 8248899977 BTC 500`\n" +
                              "• `برداشت سهام 8248899977 BTC 50`\n" +
                              "• `واریز دلار 8248899977 10000`\n" +
                              "• `برداشت دلار 8248899977 1000`";
                await bot.SendMessage(chatId, info, parseMode: ParseMode.Markdown, cancellationToken: ct);
            }
            else if (text.StartsWith("واریز سهام ") || text.StartsWith("برداشت سهام "))
            {
                var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 5 && long.TryParse(parts[4], out var qty) && qty > 0)
                {
                    var targetInput = parts[2];
                    var symbol = parts[3].ToUpper();
                    bool isAdd = text.StartsWith("واریز سهام");
                    User? target;
                    bool success = false;
                    lock (_dataLock)
                    {
                        target = FindUserByIdOrUsername(targetInput);
                        if (target != null && Market.ContainsKey(symbol))
                        {
                            if (!target.Portfolio.ContainsKey(symbol)) target.Portfolio[symbol] = 0;
                            if (isAdd)
                            {
                                target.Portfolio[symbol] += qty;
                                success = true;
                            }
                            else if (target.Portfolio[symbol] >= qty)
                            {
                                target.Portfolio[symbol] -= qty;
                                success = true;
                            }
                        }
                    }
                    if (success && target != null)
                    {
                        RequestSave();
                        string actionName = isAdd ? "واریز" : "برداشت/کسر";
                        await bot.SendMessage(chatId, $"✅ عملیات {actionName} تعداد {qty:N0} واحد سهام {symbol} برای کاربر @{target.Username} (ID: {target.UserId}) با موفقیت انجام شد!", replyMarkup: GetOwnerKeyboard(), cancellationToken: ct);
                        try { _ = Bot.SendMessage(target.UserId, $"🎁 **اعلان مدیریت:** تعداد {qty:N0} واحد سهام {symbol} به دستور مدیریت {(isAdd ? "به سبد دارایی شما واریز شد" : "از سبد دارایی شما کسر شد")}.", parseMode: ParseMode.Markdown); } catch { }
                    }
                    else
                    {
                        await bot.SendMessage(chatId, "❌ خطا: کاربر یا ارز مورد نظر یافت نشد، یا موجودی سهام کاربر برای برداشت کافی نیست.", replyMarkup: GetOwnerKeyboard(), cancellationToken: ct);
                    }
                }
                else
                {
                    await bot.SendMessage(chatId, "❌ فرمت دستور نادرست است. مثال: واریز سهام 8248899977 BTC 500", replyMarkup: GetOwnerKeyboard(), cancellationToken: ct);
                }
            }
            else if (text.StartsWith("واریز دلار ") || text.StartsWith("برداشت دلار "))
            {
                var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 4 && decimal.TryParse(parts[3], out var amount) && amount > 0)
                {
                    var targetInput = parts[2];
                    bool isAdd = text.StartsWith("واریز دلار");
                    User? target;
                    bool success = false;
                    lock (_dataLock)
                    {
                        target = FindUserByIdOrUsername(targetInput);
                        if (target != null)
                        {
                            if (isAdd)
                            {
                                target.Balance += amount;
                                success = true;
                            }
                            else if (target.Balance >= amount)
                            {
                                target.Balance -= amount;
                                success = true;
                            }
                        }
                    }
                    if (success && target != null)
                    {
                        RequestSave();
                        string actionName = isAdd ? "واریز" : "برداشت/کسر";
                        await bot.SendMessage(chatId, $"✅ عملیات {actionName} مبلغ {FmtMoney(amount)} برای کاربر @{target.Username} (ID: {target.UserId}) با موفقیت انجام شد!", replyMarkup: GetOwnerKeyboard(), cancellationToken: ct);
                        try { _ = Bot.SendMessage(target.UserId, $"🎁 **اعلان مدیریت:** مبلغ {FmtMoney(amount)} به دستور مدیریت {(isAdd ? "به موجودی نقدی شما واریز شد" : "از حساب شما کسر شد")}.", parseMode: ParseMode.Markdown); } catch { }
                    }
                    else
                    {
                        await bot.SendMessage(chatId, "❌ خطا: کاربر یافت نشد یا موجودی کاربر برای برداشت کافی نیست.", replyMarkup: GetOwnerKeyboard(), cancellationToken: ct);
                    }
                }
                else
                {
                    await bot.SendMessage(chatId, "❌ فرمت دستور نادرست است. مثال: واریز دلار 8248899977 5000", replyMarkup: GetOwnerKeyboard(), cancellationToken: ct);
                }
            }
            else if (text == "🏦 تزریق نقدینگی")
            {
                lock (_dataLock)
                {
                    foreach (var symbol in Market.Keys.ToList())
                    {
                        EnsureInitialLiquidity(symbol);
                    }
                }
                await bot.SendMessage(
                    chatId,
                    "✅ نقدینگی اولیه (سفارشات خرید و فروش تضمینی خزانه در قیمت پایه) برای تمام ارزهای بازار تزریق شد!\nاکنون کاربران می‌توانند بدون مشکل در هر ساعتی خرید و فروش کنند.",
                    replyMarkup: GetOwnerKeyboard(),
                    cancellationToken: ct
                );
            }
            else if (text == "🖼 تنظیم عکس ارز")
            {
                UserStates[chatId] = "SET_PHOTO_SYMBOL";
                await bot.SendMessage(chatId, "نماد ارز مورد نظر برای تنظیم یا تغییر عکس را وارد کنید (مثال: BTC):", cancellationToken: ct);
            }
            else if (text == "📝 تنظیم توضیحات ارز")
            {
                UserStates[chatId] = "SET_DESC_SYMBOL";
                await bot.SendMessage(chatId, "نماد ارز مورد نظر برای تغییر توضیحات را وارد کنید (مثال: BTC):", cancellationToken: ct);
            }
            else if (text == "🗑 حذف ارز")
            {
                UserStates[chatId] = "REMOVE_CURRENCY";
                await bot.SendMessage(chatId, "نماد ارزی که می‌خواهید حذف کنید را وارد کنید:", cancellationToken: ct);
            }
            else if (text == "📈 تنظیم قیمت دستی")
            {
                UserStates[chatId] = "SET_PRICE_SYMBOL";
                await bot.SendMessage(chatId, "نماد ارز مورد نظر برای تغییر قیمت را وارد کنید:", cancellationToken: ct);
            }
            else if (text == "📋 سفارشات باز")
            {
                string msg = "📋 دفترچه سفارشات فعال بازار (Order Book):\n\n";
                lock (_dataLock)
                {
                    foreach (var c in Market.Values)
                    {
                        var topOrders = c.Orders.Take(5).ToList();
                        if (topOrders.Count > 0)
                        {
                            msg += $"🔸 نماد {c.Symbol}:\n";
                            foreach (var o in topOrders)
                            {
                                string uName = o.UserId == 0 ? "خزانه مرکزی" : (Users.TryGetValue(o.UserId, out var u) ? $"@{u.Username}" : $"{o.UserId}");
                                msg += $"   [{o.Type}] {o.Quantity:N0} واحد @ {FmtPrice(o.Price)} ({uName})\n";
                            }
                            msg += "\n";
                        }
                    }
                }
                await bot.SendMessage(chatId, msg, replyMarkup: GetOwnerKeyboard(), cancellationToken: ct);
            }
            else if (text == "🔄 ریست بازار")
            {
                lock (_dataLock)
                {
                    Market.Clear();
                    EnsureDefaultCurrencies();
                }
                await bot.SendMessage(
                    chatId,
                    "⚠️ بازار کاملاً ریست شد و ارزهای اصلی (BTC, ETH, SOL, TON, DOGE) به همراه نقدینگی اولیه مجدداً ایجاد شدند!",
                    replyMarkup: GetOwnerKeyboard(),
                    cancellationToken: ct
                );
            }
            else if (text == "🎲 رویداد تصادفی")
            {
                string eventMsg = "";
                List<long> uids;
                lock (_dataLock)
                {
                    if (Market.Count > 0)
                    {
                        var random = new Random();
                        var symbol = Market.Keys.ElementAt(random.Next(Market.Count));
                        var currency = Market[symbol];
                        var change = random.Next(-30, 31);
                        var newPrice = Math.Max(0.01m, currency.PriceHistory.LastOrDefault(currency.BaseValue) * (1 + change / 100m));

                        currency.PriceHistory.Add(newPrice);
                        AdjustTreasuryOrders(currency, newPrice);

                        eventMsg = $"🎲 رویداد تصادفی بازار!\nارز {symbol} با تغییر {change:+0;-0}% مواجه شد!\n💵 قیمت جدید: {FmtPrice(newPrice)}";
                    }
                    uids = Users.Keys.Where(id => id != 0).ToList();
                }

                if (!string.IsNullOrEmpty(eventMsg))
                {
                    await bot.SendMessage(chatId, eventMsg, replyMarkup: GetOwnerKeyboard(), cancellationToken: ct);
                    foreach (var uid in uids)
                    {
                        try { await bot.SendMessage(uid, eventMsg); } catch { }
                    }
                }
            }
            else if (text == "📰 رویدادهای ویژه" || text == "رویدادها")
            {
                var keyboard = new ReplyKeyboardMarkup(new[]
                {
                    new KeyboardButton[] { "خبر مثبت", "خبر منفی", "هک", "جنگ" },
                    new KeyboardButton[] { "رکود", "رشد ناگهانی", "سقوط آزاد", "بازگشت" },
                    new KeyboardButton[] { "👑 پنل مدیریت" }
                }) { ResizeKeyboard = true };
                await bot.SendMessage(chatId, "انتخاب رویداد ویژه بازار:", replyMarkup: keyboard, cancellationToken: ct);
            }
            else if (new[] { "خبر مثبت", "خبر منفی", "هک", "جنگ", "رکود", "رشد ناگهانی", "سقوط آزاد", "بازگشت" }.Contains(text))
            {
                string eventMsg = "";
                List<long> uids;
                lock (_dataLock)
                {
                    if (Market.Count > 0)
                    {
                        var random = new Random();
                        var symbol = Market.Keys.ElementAt(random.Next(Market.Count));
                        var currency = Market[symbol];
                        decimal change = text switch
                        {
                            "خبر مثبت" => 25m,
                            "خبر منفی" => -20m,
                            "هک" => -40m,
                            "جنگ" => -35m,
                            "رکود" => -15m,
                            "رشد ناگهانی" => 50m,
                            "سقوط آزاد" => -60m,
                            "بازگشت" => 30m,
                            _ => 0m
                        };

                        var newPrice = Math.Max(0.01m, currency.PriceHistory.LastOrDefault(currency.BaseValue) * (1 + change / 100m));
                        currency.PriceHistory.Add(newPrice);
                        AdjustTreasuryOrders(currency, newPrice);

                        eventMsg = $"📰 رویداد ویژه بازار: {text}\nارز {symbol} با تغییر {change:+0;-0}% مواجه شد!\n💵 قیمت جدید: {FmtPrice(newPrice)}";
                    }
                    uids = Users.Keys.Where(id => id != 0).ToList();
                }

                if (!string.IsNullOrEmpty(eventMsg))
                {
                    await bot.SendMessage(chatId, eventMsg, replyMarkup: GetOwnerKeyboard(), cancellationToken: ct);
                    foreach (var uid in uids)
                    {
                        try { await bot.SendMessage(uid, eventMsg); } catch { }
                    }
                }
            }
            else if (text == "🏆 لیدربورد" || text == "🏆 لیدربورد برترین‌ها")
            {
                await SendLeaderboardAsync(bot, chatId, OwnerId, ct);
            }
            else if (text == "🎮 کنترل پنل دارایی پلیر" || text == "کنترل پلیر" || text == "مدیریت پلیر")
            {
                UserStates[chatId] = "ADM_CTRL_SELECT_USER";
                string info = "🎮 **کنترل پنل پیشرفته دارایی پلیر (God-Mode Player Control):**\n\n" +
                              "لطفاً پلیر مورد نظر را با یکی از ۳ روش زیر ارسال کنید:\n" +
                              "۱. 🔢 آیدی عددی (UserId): مثال: `8248899977`\n" +
                              "۲. 🏆 رتبه لیدربورد (Rank): مثال: `#1` یا `1` (نفر اول لیدربورد)\n" +
                              "۳. 👤 یوزرنیم تلگرام: مثال: `@username`\n\n" +
                              "💡 همچنین می‌توانید از دستور یک‌خطی استفاده کنید: `کنترل پلیر #1` یا `کنترل پلیر 8248899977`";

                var kbRows = new List<InlineKeyboardButton[]>();
                List<User> top3;
                lock (_dataLock)
                {
                    top3 = Users.Values
                        .Where(u => u.UserId != 0)
                        .OrderByDescending(u => u.Balance + u.Portfolio.Sum(p => p.Value * GetCurrentPrice(p.Key)))
                        .Take(3)
                        .ToList();
                }
                var btnRow = new List<InlineKeyboardButton>();
                for (int i = 0; i < top3.Count; i++)
                {
                    string rIcon = i == 0 ? "🥇" : (i == 1 ? "🥈" : "🥉");
                    btnRow.Add(InlineKeyboardButton.WithCallbackData($"{rIcon} رتبه #{i + 1}", $"ADM_CTRL_SELECT_{top3[i].UserId}"));
                }
                if (btnRow.Count > 0) kbRows.Add(btnRow.ToArray());
                kbRows.Add(new[] { InlineKeyboardButton.WithCallbackData("❌ انصراف", "ADM_CANCEL") });

                await bot.SendMessage(chatId, info, parseMode: ParseMode.Markdown, replyMarkup: new InlineKeyboardMarkup(kbRows), cancellationToken: ct);
            }
            else if (text.StartsWith("کنترل پلیر"))
            {
                var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3)
                {
                    var targetInput = parts[2];
                    User? target = FindUserByIdOrUsernameOrRank(targetInput, out _);
                    if (target != null)
                    {
                        await SendPlayerGodModeDashboardAsync(bot, chatId, target, ct);
                    }
                    else
                    {
                        await bot.SendMessage(chatId, "❌ کاربر مورد نظر یافت نشد.", replyMarkup: GetOwnerKeyboard(), cancellationToken: ct);
                    }
                }
            }
            else if (text == "🔙 بازگشت به منوی اصلی")
            {
                await bot.SendMessage(chatId, "به منوی کاربری بازگشتید.", replyMarkup: GetUserKeyboard(OwnerId), cancellationToken: ct);
            }
        }

        // ===================== USER COMMANDS =====================
        private static async Task HandleUserCommands(ITelegramBotClient bot, Message message, string text, CancellationToken ct)
        {
            var chatId = message.Chat.Id;
            var userId = message.From?.Id ?? chatId;
            User user;
            lock (_dataLock)
            {
                if (!Users.TryGetValue(userId, out var u) || u == null)
                {
                    u = new User { UserId = userId, Username = "unknown", Balance = 5000m };
                    Users[userId] = u;
                }
                user = u;
            }

            // مدیریت /start و کدهای رفرال
            if (text == "/start" || text.StartsWith("/start ") || text == "start" || text == "شروع")
            {
                if (text.StartsWith("/start "))
                {
                    var code = text.Split(' ')[1].Trim();
                    long targetReferrerId = 0;
                    lock (_dataLock)
                    {
                        var referrer = Users.Values.FirstOrDefault(u => u.ReferralCode.Equals(code, StringComparison.OrdinalIgnoreCase));
                        if (referrer != null && referrer.UserId != userId && !user.HasUsedReferral && user.TotalTrades == 0 && user.XP == 0 && user.Balance == 5000m)
                        {
                            user.HasUsedReferral = true;
                            referrer.Balance += 500m;
                            referrer.Referrals++;
                            user.Balance += 200m;
                            targetReferrerId = referrer.UserId;
                        }
                    }
                    if (targetReferrerId != 0)
                    {
                        try { await bot.SendMessage(targetReferrerId, $"🎉 تبریک! یک کاربر جدید با کد دعوت شما عضو شد! +$500 پاداش به موجودی شما اضافه شد."); } catch { }
                    }
                }

                string welcome = $"🌟 به بات معاملاتی نائومی (Naomi) خوش آمدید!\n\n" +
                                 $"💰 موجودی نقدی شما: {FmtMoney(user.Balance)}\n" +
                                 $"📈 سطح کاربری: Level {user.Level} ({user.XP}/{user.Level * 100} XP)\n" +
                                 $"🎁 کد دعوت اختصاصی شما: {user.ReferralCode}\n\n" +
                                 $"از دکمه‌های منوی زیر یا دکمه‌های شیشه‌ای برای مشاهده بازار، معامله سریع و دریافت نمودار گرافیکی استفاده کنید:";

                await bot.SendMessage(chatId, welcome, replyMarkup: GetUserKeyboard(userId), cancellationToken: ct);
                return;
            }

            // راهنما
            if (text == "راهنما" || text == "help" || text == "/help" || text == "📖 راهنمای بات")
            {
                await bot.SendMessage(chatId, GetCompleteHelpText(), replyMarkup: GetUserKeyboard(userId), cancellationToken: ct);
                return;
            }

            // بازار و قیمت‌ها
            if (text == "بازار" || text == "📊 بازار و قیمت‌ها")
            {
                await SendMarketOverviewAsync(bot, chatId, ct);
                return;
            }

            // تابلوی معاملاتی لایو (بورس P2P)
            if (text == "تابلو" || text == "تابلوی معاملاتی" || text == "📋 تابلوی معاملاتی")
            {
                await SendBoardSelectorAsync(bot, chatId, ct);
                return;
            }

            // خرید سهام
            if (text == "خرید سهام" || text == "🛒 خرید سهام")
            {
                await SendBuyMenuSelectorAsync(bot, chatId, ct);
                return;
            }

            // فروش سهام
            if (text == "فروش سهام" || text == "💰 فروش سهام")
            {
                await SendSellMenuSelectorAsync(bot, chatId, ct);
                return;
            }

            // نمودار و چارت
            if (text == "نمودار و چارت" || text == "📈 نمودار و چارت")
            {
                await SendChartSelectorAsync(bot, chatId, ct);
                return;
            }

            // لیدربورد
            if (text == "لیدربورد" || text == "🏆 لیدربورد برترین‌ها" || text == "🏆 لیدربورد")
            {
                await SendLeaderboardAsync(bot, chatId, userId, ct);
                return;
            }

            // پرتفو
            if (text == "پرتفو" || text == "پورتفولیو" || text == "💼 پرتفو من" || text == "💼 پورتفولیو من")
            {
                await SendUserPortfolioAsync(bot, chatId, userId, ct);
                return;
            }

            // موجودی
            if (text == "موجودی" || text == "💰 موجودی من")
            {
                string info = $"💰 وضعیت مالی و حساب کاربری شما (@{user.Username}):\n\n" +
                              $"💵 موجودی دلار نقدی: {FmtMoney(user.Balance)}\n" +
                              $"📈 سطح کاربری: Level {user.Level} ({user.XP}/{user.Level * 100} XP)\n" +
                              $"🔄 تعداد کل معاملات: {user.TotalTrades}\n" +
                              $"🎁 کد دعوت اختصاصی: {user.ReferralCode}\n" +
                              $"👥 دوستان دعوت‌شده: {user.Referrals} نفر";

                var kb = new InlineKeyboardMarkup(new[]
                {
                    InlineKeyboardButton.WithCallbackData("🎁 دریافت لینک دعوت", "SHOW_REFERRAL")
                });

                await bot.SendMessage(chatId, info, replyMarkup: kb, cancellationToken: ct);
                return;
            }

            // دعوت دوستان
            if (text == "دعوت" || text == "🎁 دعوت دوستان")
            {
                string refMsg = $"🎁 کد دعوت اختصاصی شما: {user.ReferralCode}\n\n" +
                                $"🔗 لینک دعوت مستقیم:\n" +
                                $"https://t.me/{BotUsername}?start={user.ReferralCode}\n\n" +
                                $"با دعوت هر دوست با لینک یا کد بالا، شما مبلغ {FmtMoney(500m)} و دوست شما مبلغ {FmtMoney(200m)} پاداش اولیه دریافت می‌کند!\n" +
                                $"👥 تعداد دوستان دعوت‌شده: {user.Referrals} نفر";

                await bot.SendMessage(chatId, refMsg, replyMarkup: GetUserKeyboard(userId), cancellationToken: ct);
                return;
            }

            // دستورات متنی (خرید / فروش / چارت / نمودار / پنل ارز / دعوت دستی)
            if (text.StartsWith("نمودار "))
            {
                var parts = text.Split(' ');
                if (parts.Length >= 2)
                {
                    var symbol = parts[1].ToUpper();
                    string chart = "";
                    lock (_dataLock)
                    {
                        if (Market.TryGetValue(symbol, out var c))
                        {
                            chart = $"📉 تاریخچه قیمت‌های {symbol} (۱۰ قیمت اخیر - $):\n" +
                                    string.Join(" → ", c.PriceHistory.TakeLast(10).Select(p => FmtPrice(p)));
                        }
                    }
                    if (!string.IsNullOrEmpty(chart))
                    {
                        await bot.SendMessage(chatId, chart, replyMarkup: GetUserKeyboard(userId), cancellationToken: ct);
                        return;
                    }
                }
            }
            else if (text.StartsWith("چارت "))
            {
                var parts = text.Split(' ');
                if (parts.Length >= 2)
                {
                    var symbol = parts[1].ToUpper();
                    await SendGraphicChartAsync(bot, chatId, symbol, ct);
                    return;
                }
            }
            else if (text.StartsWith("تابلو ") || text.StartsWith("تابلوی معاملاتی "))
            {
                var parts = text.Split(' ');
                if (parts.Length >= 2)
                {
                    var symbol = parts.Last().ToUpper();
                    await SendTradingBoardAsync(bot, chatId, symbol, ct);
                    return;
                }
            }
            else if (text.StartsWith("پنل ارز "))
            {
                var parts = text.Split(' ');
                if (parts.Length >= 3)
                {
                    var symbol = parts[2].ToUpper();
                    await SendSymbolCardAsync(bot, chatId, symbol, ct);
                    return;
                }
            }
            else if (text.StartsWith("خرید "))
            {
                var parts = text.Split(' ');
                if (parts.Length == 4 && long.TryParse(parts[2], out var qty) && decimal.TryParse(parts[3], out var price))
                {
                    var symbol = parts[1].ToUpper();
                    bool success = false;
                    decimal bal = 0m;
                    lock (_dataLock)
                    {
                        if (Market.TryGetValue(symbol, out var c))
                        {
                            if (user.Balance >= price * qty)
                            {
                                var order = new Order { UserId = userId, Type = "BUY", Price = price, Quantity = qty, Timestamp = DateTime.UtcNow };
                                c.Orders.Add(order);
                                MatchOrders(symbol, userId);
                                bal = Users[userId].Balance;
                                success = true;
                            }
                        }
                    }
                    if (success)
                    {
                        await bot.SendMessage(chatId, $"✅ سفارش خرید {qty:N0} واحد {symbol} به قیمت واحد {FmtPrice(price)} ثبت و بررسی شد!\n💰 موجودی دلار: {FmtMoney(bal)}", replyMarkup: GetUserKeyboard(userId), cancellationToken: ct);
                        return;
                    }
                    else
                    {
                        await bot.SendMessage(chatId, $"❌ موجودی نقدی دلار شما کافی نیست.\nمبلغ مورد نیاز: {FmtMoney(price * qty)}\nموجودی شما: {FmtMoney(user.Balance)}", cancellationToken: ct);
                        return;
                    }
                }
            }
            else if (text.StartsWith("فروش "))
            {
                var parts = text.Split(' ');
                if (parts.Length == 4 && long.TryParse(parts[2], out var qty) && decimal.TryParse(parts[3], out var price))
                {
                    var symbol = parts[1].ToUpper();
                    bool success = false;
                    long hasStock = 0;
                    decimal bal = 0m;
                    lock (_dataLock)
                    {
                        if (Market.TryGetValue(symbol, out var c))
                        {
                            hasStock = user.Portfolio.TryGetValue(symbol, out var sq) ? sq : 0;
                            if (hasStock >= qty)
                            {
                                var order = new Order { UserId = userId, Type = "SELL", Price = price, Quantity = qty, Timestamp = DateTime.UtcNow };
                                c.Orders.Add(order);
                                MatchOrders(symbol, userId);
                                bal = Users[userId].Balance;
                                success = true;
                            }
                        }
                    }
                    if (success)
                    {
                        await bot.SendMessage(chatId, $"✅ سفارش فروش {qty:N0} واحد {symbol} به قیمت واحد {FmtPrice(price)} ثبت و بررسی شد!\n💰 موجودی دلار: {FmtMoney(bal)}", replyMarkup: GetUserKeyboard(userId), cancellationToken: ct);
                        return;
                    }
                    else
                    {
                        await bot.SendMessage(chatId, $"❌ موجودی سهام شما از ارز {symbol} کافی نیست. موجودی: {hasStock:N0} واحد", cancellationToken: ct);
                        return;
                    }
                }
            }
            else if (text.StartsWith("دعوت "))
            {
                var parts = text.Split(' ');
                if (parts.Length >= 2)
                {
                    var code = parts[1].Trim();
                    long targetReferrerId = 0;
                    lock (_dataLock)
                    {
                        var referrer = Users.Values.FirstOrDefault(u => u.ReferralCode.Equals(code, StringComparison.OrdinalIgnoreCase));
                        if (referrer != null && referrer.UserId != userId && !user.HasUsedReferral && user.TotalTrades == 0 && user.XP == 0 && user.Balance == 5000m)
                        {
                            user.HasUsedReferral = true;
                            referrer.Balance += 500m;
                            referrer.Referrals++;
                            user.Balance += 200m;
                            targetReferrerId = referrer.UserId;
                        }
                    }
                    if (targetReferrerId != 0)
                    {
                        await bot.SendMessage(chatId, $"✅ با موفقیت با کد دعوت ثبت شدید! مبلغ {FmtMoney(200m)} به موجودی شما اضافه شد.", replyMarkup: GetUserKeyboard(userId), cancellationToken: ct);
                        try
                        {
                            await bot.SendMessage(targetReferrerId, $"🎉 یکی از دوستان شما با کد دعوتتان عضو شد! پاداش {FmtMoney(500m)} واریز شد.");
                        }
                        catch { }
                        return;
                    }
                    else
                    {
                        await bot.SendMessage(chatId, "❌ خطا: کد دعوت نامعتبر است، یا شما قبلاً از کد دعوت استفاده کرده‌اید و کاربر جدید نیستید!", replyMarkup: GetUserKeyboard(userId), cancellationToken: ct);
                        return;
                    }
                }
            }

            // اگر دستور متنی شناخته نشد:
            await bot.SendMessage(
                chatId,
                "💡 دستور نامشخص است. لطفاً از دکمه‌های منوی زیر استفاده کنید یا دکمه 📖 راهنمای بات را بزنید.",
                replyMarkup: GetUserKeyboard(userId),
                cancellationToken: ct
            );
        }

        // ===================== CALLBACK QUERIES (اینلاین تعاملی) =====================
        private static async Task HandleCallbackQueryAsync(ITelegramBotClient bot, CallbackQuery callbackQuery, CancellationToken ct)
        {
            try
            {
                await bot.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: ct);
            }
            catch { }

            var data = callbackQuery.Data ?? "";
            var userId = callbackQuery.From.Id;
            long chatId = callbackQuery.Message?.Chat.Id ?? userId;

            User user;
            lock (_dataLock)
            {
                if (!Users.TryGetValue(userId, out var u) || u == null)
                {
                    u = new User
                    {
                        UserId = userId,
                        Username = callbackQuery.From.Username ?? "unknown",
                        Balance = 5000m,
                        Level = 1,
                        XP = 0
                    };
                    u.DeviceFingerprints.Add(userId % 100000);
                    u.ReferralCode = "REF" + userId.ToString().Substring(Math.Max(0, userId.ToString().Length - 6));
                    Users[userId] = u;
                }
                user = u;
            }

            if (data.StartsWith("VIEW_SYMBOL_"))
            {
                var symbol = data.Split('_')[2];
                await SendSymbolCardAsync(bot, chatId, symbol, ct);
            }
            else if (data.StartsWith("BOARD_"))
            {
                var symbol = data.Split('_')[1];
                await SendTradingBoardAsync(bot, chatId, symbol, ct);
            }
            else if (data.StartsWith("MY_ORDERS_"))
            {
                var symbol = data.Split('_')[2];
                await SendUserOpenOrdersAsync(bot, chatId, userId, symbol, ct);
            }
            else if (data.StartsWith("CANCEL_ORDER_"))
            {
                var parts = data.Split('_');
                var symbol = parts[2];
                if (long.TryParse(parts[3], out var orderId))
                {
                    bool cancelled = false;
                    lock (_dataLock)
                    {
                        if (Market.TryGetValue(symbol, out var c))
                        {
                            var o = c.Orders.FirstOrDefault(x => x.UserId == userId && x.Timestamp.Ticks == orderId);
                            if (o != null)
                            {
                                c.Orders.Remove(o);
                                cancelled = true;
                            }
                        }
                    }
                    if (cancelled)
                    {
                        RequestSave();
                        await bot.SendMessage(chatId, $"✅ سفارش شما با موفقیت از تابلوی معاملاتی {symbol} لغو شد.", cancellationToken: ct);
                        await SendUserOpenOrdersAsync(bot, chatId, userId, symbol, ct);
                    }
                    else
                    {
                        await bot.SendMessage(chatId, "❌ سفارش مورد نظر یافت نشد یا قبلاً معامله شده است.", cancellationToken: ct);
                    }
                }
            }
            else if (data.StartsWith("CHART_TF_"))
            {
                var parts = data.Split('_');
                var symbol = parts[2];
                var tf = parts[3];
                await SendGraphicChartAsync(bot, chatId, symbol, tf, ct);
            }
            else if (data.StartsWith("CHART_"))
            {
                var symbol = data.Split('_')[1];
                await SendGraphicChartAsync(bot, chatId, symbol, "ALL", ct);
            }
            else if (data.StartsWith("LIMIT_BUY_INPUT_"))
            {
                var symbol = data.Split('_')[3];
                UserStates[chatId] = $"LIMIT_BUY_QTY_{symbol}";
                await bot.SendMessage(chatId, $"🛒 لطفاً تعداد واحد مورد نظر برای سفارش‌گذاری خرید (Limit) روی تابلو {symbol} را وارد کنید:", cancellationToken: ct);
            }
            else if (data.StartsWith("LIMIT_SELL_INPUT_"))
            {
                var symbol = data.Split('_')[3];
                UserStates[chatId] = $"LIMIT_SELL_QTY_{symbol}";
                var hasStock = user.Portfolio.TryGetValue(symbol, out var sq) ? sq : 0;
                await bot.SendMessage(chatId, $"💰 لطفاً تعداد واحد مورد نظر برای سفارش‌گذاری فروش (Limit) روی تابلو {symbol} را وارد کنید (موجودی: {hasStock:N0} واحد):", cancellationToken: ct);
            }
            else if (data.StartsWith("BUY_MENU_"))
            {
                var symbol = data.Split('_')[2];
                decimal price;
                lock (_dataLock)
                {
                    if (!Market.TryGetValue(symbol, out _)) return;
                    price = GetCurrentPrice(symbol);
                }

                string msg = $"🛒 خرید سریع سهام {symbol} — قیمت لحظه‌ای واحد: {FmtPrice(price)}\n\n" +
                             $"💵 موجودی دلار نقدی شما: {FmtMoney(user.Balance)}\n" +
                             $"لطفاً مقدار مورد نظر برای خرید فوری در قیمت بازار را انتخاب کنید:";

                var kb = new InlineKeyboardMarkup(new[]
                {
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("🛒 ۱ واحد", $"QUICK_BUY_{symbol}_1"),
                        InlineKeyboardButton.WithCallbackData("🛒 ۳ واحد", $"QUICK_BUY_{symbol}_3"),
                        InlineKeyboardButton.WithCallbackData("🛒 ۵ واحد", $"QUICK_BUY_{symbol}_5")
                    },
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("🛒 ۱۰ واحد", $"QUICK_BUY_{symbol}_10"),
                        InlineKeyboardButton.WithCallbackData("🛒 ۲۵ واحد", $"QUICK_BUY_{symbol}_25"),
                        InlineKeyboardButton.WithCallbackData("🛒 ۵۰ واحد", $"QUICK_BUY_{symbol}_50")
                    },
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("✍️ خرید مقدار دلخواه", $"CUSTOM_BUY_INPUT_{symbol}"),
                        InlineKeyboardButton.WithCallbackData("🔙 بازگشت", $"VIEW_SYMBOL_{symbol}")
                    }
                });

                await bot.SendMessage(chatId, msg, replyMarkup: kb, cancellationToken: ct);
            }
            else if (data.StartsWith("SELL_MENU_"))
            {
                var symbol = data.Split('_')[2];
                long hasStock = 0;
                decimal price = 0m;
                lock (_dataLock)
                {
                    if (!Market.TryGetValue(symbol, out _)) return;
                    hasStock = user.Portfolio.TryGetValue(symbol, out var sq) ? sq : 0;
                    price = Math.Max(0.01m, GetCurrentPrice(symbol) * 0.95m);
                }

                string msg = $"💰 فروش سریع سهام {symbol} — قیمت نقدشوندگی واحد: {FmtPrice(price)}\n\n" +
                             $"📦 موجودی سهام شما: {hasStock:N0} واحد\n" +
                             $"لطفاً مقدار مورد نظر برای فروش فوری را انتخاب کنید:";

                var kb = new InlineKeyboardMarkup(new[]
                {
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("💰 ۱ واحد", $"QUICK_SELL_{symbol}_1"),
                        InlineKeyboardButton.WithCallbackData("💰 ۳ واحد", $"QUICK_SELL_{symbol}_3"),
                        InlineKeyboardButton.WithCallbackData("💰 ۵ واحد", $"QUICK_SELL_{symbol}_5")
                    },
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("💰 ۱۰ واحد", $"QUICK_SELL_{symbol}_10"),
                        InlineKeyboardButton.WithCallbackData("💰 ۲۵ واحد", $"QUICK_SELL_{symbol}_25"),
                        InlineKeyboardButton.WithCallbackData("💰 ۵۰ واحد", $"QUICK_SELL_{symbol}_50")
                    },
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("🔥 فروش همه موجودی", $"SELL_ALL_{symbol}"),
                        InlineKeyboardButton.WithCallbackData("✍️ فروش مقدار دلخواه", $"CUSTOM_SELL_INPUT_{symbol}")
                    },
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("🔙 بازگشت به پنل ارز", $"VIEW_SYMBOL_{symbol}")
                    }
                });

                await bot.SendMessage(chatId, msg, replyMarkup: kb, cancellationToken: ct);
            }
            else if (data.StartsWith("CUSTOM_BUY_INPUT_"))
            {
                var symbol = data.Split('_')[3];
                UserStates[chatId] = $"CUSTOM_BUY_QTY_{symbol}";
                decimal price;
                lock (_dataLock) { price = GetCurrentPrice(symbol); }
                await bot.SendMessage(
                    chatId,
                    $"🛒 لطفاً تعداد واحد مورد نظر برای خرید فوری {symbol} به قیمت لحظه‌ای واحد ({FmtPrice(price)}) را به صورت یک عدد وارد کنید (مثال: 3 یا 15):",
                    cancellationToken: ct
                );
            }
            else if (data.StartsWith("CUSTOM_SELL_INPUT_"))
            {
                var symbol = data.Split('_')[3];
                UserStates[chatId] = $"CUSTOM_SELL_QTY_{symbol}";
                long hasStock;
                lock (_dataLock) { hasStock = user.Portfolio.TryGetValue(symbol, out var sq) ? sq : 0; }
                await bot.SendMessage(
                    chatId,
                    $"💰 لطفاً تعداد واحد مورد نظر برای فروش فوری {symbol} را وارد کنید (موجودی سهام شما: {hasStock:N0} واحد):",
                    cancellationToken: ct
                );
            }
            else if (data.StartsWith("QUICK_BUY_"))
            {
                var parts = data.Split('_');
                var symbol = parts[2];
                if (long.TryParse(parts[3], out var qty))
                {
                    bool canBuy = false;
                    decimal price = 0m;
                    decimal totalCost = 0m;
                    lock (_dataLock)
                    {
                        if (Market.TryGetValue(symbol, out var c))
                        {
                            price = GetCurrentPrice(symbol);
                            totalCost = price * qty * 1.01m;
                            if (GetAvailableCash(user) >= totalCost)
                            {
                                canBuy = true;
                                var order = new Order { UserId = userId, Type = "BUY", Price = price, Quantity = qty, Timestamp = DateTime.UtcNow };
                                c.Orders.Add(order);
                                MatchOrders(symbol, userId);
                            }
                        }
                    }

                    if (!canBuy)
                    {
                        await bot.SendMessage(chatId, $"❌ موجودی نقدی دلار آزاد شما کافی نیست.\nمبلغ مورد نیاز با کارمزد: {FmtMoney(totalCost)}\nموجودی آزاد شما: {FmtMoney(GetAvailableCash(user))}", cancellationToken: ct);
                        return;
                    }

                    RequestSave();
                    await bot.SendMessage(
                        chatId,
                        $"✅ سفارش فوری خرید {qty:N0} واحد {symbol} به قیمت واحد {FmtPrice(price)} ثبت و معامله شد!\n💰 موجودی جدید دلار شما: {FmtMoney(user.Balance)}",
                        replyMarkup: userId == OwnerId ? GetOwnerKeyboard() : GetUserKeyboard(userId),
                        cancellationToken: ct
                    );
                }
            }
            else if (data.StartsWith("QUICK_SELL_"))
            {
                var parts = data.Split('_');
                var symbol = parts[2];
                if (long.TryParse(parts[3], out var qty))
                {
                    bool canSell = false;
                    long hasStock = 0;
                    decimal price = 0m;
                    lock (_dataLock)
                    {
                        if (Market.TryGetValue(symbol, out var c))
                        {
                            hasStock = GetAvailableStock(user, symbol);
                            if (hasStock >= qty)
                            {
                                canSell = true;
                                price = Math.Max(0.01m, GetCurrentPrice(symbol) * 0.95m);
                                var order = new Order { UserId = userId, Type = "SELL", Price = price, Quantity = qty, Timestamp = DateTime.UtcNow };
                                c.Orders.Add(order);
                                MatchOrders(symbol, userId);
                            }
                        }
                    }

                    if (!canSell)
                    {
                        await bot.SendMessage(chatId, $"❌ موجودی سهام آزاد {symbol} شما کافی نیست. موجودی آزاد شما: {hasStock:N0} واحد", cancellationToken: ct);
                        return;
                    }

                    RequestSave();
                    await bot.SendMessage(
                        chatId,
                        $"✅ سفارش فوری فروش {qty:N0} واحد {symbol} به قیمت واحد {FmtPrice(price)} معامله شد!\n💰 موجودی جدید دلار شما: {FmtMoney(user.Balance)}",
                        replyMarkup: userId == OwnerId ? GetOwnerKeyboard() : GetUserKeyboard(userId),
                        cancellationToken: ct
                    );
                }
            }
            else if (data.StartsWith("SELL_ALL_"))
            {
                var symbol = data.Split('_')[2];
                bool canSell = false;
                long hasStock = 0;
                decimal price = 0m;
                lock (_dataLock)
                {
                    if (Market.TryGetValue(symbol, out var c))
                    {
                        hasStock = GetAvailableStock(user, symbol);
                        if (hasStock > 0)
                        {
                            canSell = true;
                            price = Math.Max(0.01m, GetCurrentPrice(symbol) * 0.95m);
                            var order = new Order { UserId = userId, Type = "SELL", Price = price, Quantity = hasStock, Timestamp = DateTime.UtcNow };
                            c.Orders.Add(order);
                            MatchOrders(symbol, userId);
                        }
                    }
                }

                if (!canSell)
                {
                    await bot.SendMessage(chatId, $"❌ شما هیچ موجودی سهام آزاد از ارز {symbol} برای فروش ندارید.", cancellationToken: ct);
                    return;
                }

                RequestSave();
                await bot.SendMessage(
                    chatId,
                    $"✅ تمام موجودی {symbol} شما ({hasStock:N0} واحد) به قیمت واحد {FmtPrice(price)} فروخته شد!\n💰 موجودی جدید دلار شما: {FmtMoney(user.Balance)}",
                    replyMarkup: userId == OwnerId ? GetOwnerKeyboard() : GetUserKeyboard(userId),
                    cancellationToken: ct
                );
            }
            else if (data == "SHOW_REFERRAL")
            {
                string refMsg = $"🎁 کد دعوت اختصاصی شما: {user.ReferralCode}\n\n" +
                                $"🔗 لینک دعوت مستقیم:\n" +
                                $"https://t.me/{BotUsername}?start={user.ReferralCode}\n\n" +
                                $"با دعوت هر دوست، شما مبلغ {FmtMoney(500m)} و دوست شما مبلغ {FmtMoney(200m)} پاداش اولیه دریافت می‌کند!\n" +
                                $"👥 دوستان دعوت‌شده: {user.Referrals} نفر";
                await bot.SendMessage(chatId, refMsg, cancellationToken: ct);
            }
            else if (data == "REFRESH_PORTFOLIO")
            {
                await SendUserPortfolioAsync(bot, chatId, userId, ct);
            }
            else if (data == "VIEW_MARKET")
            {
                await SendMarketOverviewAsync(bot, chatId, ct);
            }
            else if (data.StartsWith("ADM_CTRL_SELECT_"))
            {
                if (userId != OwnerId) return;
                var targetId = long.Parse(data.Split('_')[3]);
                User? target = null;
                lock (_dataLock) { Users.TryGetValue(targetId, out target); }
                if (target != null) await SendPlayerGodModeDashboardAsync(bot, chatId, target, ct);
            }
            else if (data.StartsWith("ADM_CTRL_ADD_CASH_"))
            {
                if (userId != OwnerId) return;
                var targetId = data.Split('_')[4];
                UserStates[chatId] = $"ADM_CTRL_ADD_CASH_AMT_{targetId}";
                await bot.SendMessage(chatId, "💵 چه مقدار دلار ($) می‌خواهید به موجودی کاربر اضافه کنید؟ (مثال: 50000):", cancellationToken: ct);
            }
            else if (data.StartsWith("ADM_CTRL_SUB_CASH_"))
            {
                if (userId != OwnerId) return;
                var targetId = data.Split('_')[4];
                UserStates[chatId] = $"ADM_CTRL_SUB_CASH_AMT_{targetId}";
                await bot.SendMessage(chatId, "💸 چه مقدار دلار ($) می‌خواهید از موجودی کاربر کسر کنید؟ (مثال: 5000):", cancellationToken: ct);
            }
            else if (data.StartsWith("ADM_CTRL_ADD_STOCK_"))
            {
                if (userId != OwnerId) return;
                var targetId = data.Split('_')[4];
                var rows = new List<InlineKeyboardButton[]>();
                lock (_dataLock)
                {
                    foreach (var sym in Market.Keys)
                    {
                        rows.Add(new[] { InlineKeyboardButton.WithCallbackData($"📦 واریز {sym}", $"ADM_CTRL_ADD_SYM_{targetId}_{sym}") });
                    }
                }
                await bot.SendMessage(chatId, "📦 ارزی که می‌خواهید به سبد کاربر واریز کنید را انتخاب کنید:", replyMarkup: new InlineKeyboardMarkup(rows), cancellationToken: ct);
            }
            else if (data.StartsWith("ADM_CTRL_ADD_SYM_"))
            {
                if (userId != OwnerId) return;
                var parts = data.Split('_');
                var targetId = parts[4];
                var symbol = parts[5];
                UserStates[chatId] = $"ADM_CTRL_ADD_STOCK_QTY_{targetId}_{symbol}";
                await bot.SendMessage(chatId, $"📦 چه تعداد واحد سهام {symbol} می‌خواهید به کاربر واریز کنید؟ (مثال: 1000):", cancellationToken: ct);
            }
            else if (data.StartsWith("ADM_CTRL_SUB_STOCK_"))
            {
                if (userId != OwnerId) return;
                var targetId = long.Parse(data.Split('_')[4]);
                var rows = new List<InlineKeyboardButton[]>();
                lock (_dataLock)
                {
                    if (Users.TryGetValue(targetId, out var u))
                    {
                        foreach (var p in u.Portfolio.Where(x => x.Value > 0))
                        {
                            rows.Add(new[] { InlineKeyboardButton.WithCallbackData($"🗑 کسر {p.Key} (موجودی: {p.Value:N0})", $"ADM_CTRL_SUB_SYM_{targetId}_{p.Key}") });
                        }
                    }
                }
                if (rows.Count == 0)
                {
                    await bot.SendMessage(chatId, "🔹 سبد سهام این کاربر خالی است.", cancellationToken: ct);
                }
                else
                {
                    await bot.SendMessage(chatId, "🗑 سهامی که می‌خواهید از کاربر کسر کنید را انتخاب کنید:", replyMarkup: new InlineKeyboardMarkup(rows), cancellationToken: ct);
                }
            }
            else if (data.StartsWith("ADM_CTRL_SUB_SYM_"))
            {
                if (userId != OwnerId) return;
                var parts = data.Split('_');
                var targetId = parts[4];
                var symbol = parts[5];
                UserStates[chatId] = $"ADM_CTRL_SUB_STOCK_QTY_{targetId}_{symbol}";
                await bot.SendMessage(chatId, $"🗑 چه تعداد واحد سهام {symbol} می‌خواهید از کاربر کسر کنید؟:", cancellationToken: ct);
            }
            else if (data.StartsWith("ADM_CTRL_RESET_"))
            {
                if (userId != OwnerId) return;
                var targetId = long.Parse(data.Split('_')[3]);
                User? target = null;
                lock (_dataLock)
                {
                    if (Users.TryGetValue(targetId, out var u))
                    {
                        u.Balance = 5000m;
                        u.Portfolio.Clear();
                        u.CostBasis.Clear();
                        u.TotalProfit = 0m;
                        u.TotalTrades = 0;
                        u.SuccessfulTrades = 0;
                        u.XP = 0;
                        u.Level = 1;
                        foreach (var c in Market.Values) c.Orders.RemoveAll(o => o.UserId == targetId);
                        target = u;
                    }
                }
                if (target != null)
                {
                    RequestSave();
                    await bot.SendMessage(chatId, $"✅ حساب کاربری @{target.Username} به طور کامل ریست شد!", cancellationToken: ct);
                    await SendPlayerGodModeDashboardAsync(bot, chatId, target, ct);
                }
            }
            else if (data.StartsWith("ADM_CTRL_CLEAR_STOCK_"))
            {
                if (userId != OwnerId) return;
                var targetId = long.Parse(data.Split('_')[4]);
                User? target = null;
                lock (_dataLock)
                {
                    if (Users.TryGetValue(targetId, out var u))
                    {
                        u.Portfolio.Clear();
                        u.CostBasis.Clear();
                        foreach (var c in Market.Values) c.Orders.RemoveAll(o => o.UserId == targetId && o.Type == "SELL");
                        target = u;
                    }
                }
                if (target != null)
                {
                    RequestSave();
                    await bot.SendMessage(chatId, $"✅ تمام سهام‌های سبد کاربر @{target.Username} پاک شد!", cancellationToken: ct);
                    await SendPlayerGodModeDashboardAsync(bot, chatId, target, ct);
                }
            }
            else if (data.StartsWith("ADM_CTRL_BAN_"))
            {
                if (userId != OwnerId) return;
                var targetId = long.Parse(data.Split('_')[3]);
                User? target = null;
                lock (_dataLock)
                {
                    if (Users.TryGetValue(targetId, out var u))
                    {
                        u.IsBanned = !u.IsBanned;
                        target = u;
                    }
                }
                if (target != null)
                {
                    RequestSave();
                    await SendPlayerGodModeDashboardAsync(bot, chatId, target, ct);
                }
            }
            else if (data.StartsWith("ADM_CTRL_BOOST_"))
            {
                if (userId != OwnerId) return;
                var targetId = long.Parse(data.Split('_')[3]);
                User? target = null;
                lock (_dataLock)
                {
                    if (Users.TryGetValue(targetId, out var u))
                    {
                        u.XP += 1000;
                        u.Level += 2;
                        target = u;
                    }
                }
                if (target != null)
                {
                    RequestSave();
                    await bot.SendMessage(chatId, $"⚡ تعداد +1000 XP و +2 Level به کاربر @{target.Username} اهدا شد!", cancellationToken: ct);
                    await SendPlayerGodModeDashboardAsync(bot, chatId, target, ct);
                }
            }
            else if (data.StartsWith("ADM_CTRL_REFRESH_"))
            {
                if (userId != OwnerId) return;
                var targetId = long.Parse(data.Split('_')[3]);
                User? target = null;
                lock (_dataLock) { Users.TryGetValue(targetId, out target); }
                if (target != null) await SendPlayerGodModeDashboardAsync(bot, chatId, target, ct);
            }
            else if (data == "ADM_CANCEL")
            {
                await bot.SendMessage(chatId, "❌ عملیات انصراف شد.", replyMarkup: GetOwnerKeyboard(), cancellationToken: ct);
            }
        }

        // ===================== HELPER METHODS & UI CARDS =====================
        private static async Task SendBoardSelectorAsync(ITelegramBotClient bot, long chatId, CancellationToken ct)
        {
            string msg = "📋 **منوی انتخاب تابلوی معاملاتی لایو (بورس همتا به همتا - P2P):**\n\n" +
                         "برای مشاهده صف خرید و فروش، کمترین قیمت فروشنده و بیشترین قیمت خریدار هر ارز، روی نماد مورد نظر کلیک کنید:";

            List<InlineKeyboardButton> symbolButtons;
            lock (_dataLock)
            {
                symbolButtons = Market.Keys.Select(sym =>
                    InlineKeyboardButton.WithCallbackData($"📋 تابلوی {sym} ({FmtPrice(GetCurrentPrice(sym))})", $"BOARD_{sym}")
                ).ToList();
            }

            var rows = new List<InlineKeyboardButton[]>();
            for (int i = 0; i < symbolButtons.Count; i += 2)
            {
                if (i + 1 < symbolButtons.Count)
                    rows.Add(new[] { symbolButtons[i], symbolButtons[i + 1] });
                else
                    rows.Add(new[] { symbolButtons[i] });
            }

            await bot.SendMessage(chatId, msg, parseMode: ParseMode.Markdown, replyMarkup: new InlineKeyboardMarkup(rows), cancellationToken: ct);
        }

        private static async Task SendTradingBoardAsync(ITelegramBotClient bot, long chatId, string symbol, CancellationToken ct)
        {
            Currency? cCopy = null;
            List<Order> buyOrders = new();
            List<Order> sellOrders = new();
            decimal curPrice = 0m;
            long ipoLeft = 0;

            lock (_dataLock)
            {
                if (Market.TryGetValue(symbol, out var c))
                {
                    cCopy = c;
                    curPrice = GetCurrentPrice(symbol);
                    buyOrders = c.Orders.Where(o => o.Type == "BUY" && o.Quantity > 0).OrderByDescending(o => o.Price).Take(5).ToList();
                    sellOrders = c.Orders.Where(o => o.Type == "SELL" && o.Quantity > 0).OrderBy(o => o.Price).Take(5).ToList();
                    ipoLeft = c.Orders.Where(o => o.Type == "SELL" && o.UserId == 0).Sum(o => o.Quantity);
                }
            }

            if (cCopy == null)
            {
                await bot.SendMessage(chatId, $"❌ ارز {symbol} یافت نشد.", cancellationToken: ct);
                return;
            }

            string msg = $"📋 تابلوی معاملات زنده — {cCopy.Symbol} ({cCopy.Description})\n\n" +
                         $"💵 آخرین قیمت معامله: {FmtPrice(curPrice)}\n" +
                         $"💎 قیمت پایه اولیه: {FmtPrice(cCopy.BaseValue)}\n" +
                         $"🏦 سهام باقی‌مانده خزانه: {ipoLeft:N0} واحد\n\n" +
                         $"━━━━━━━━━━━━━━━━━━━━━━\n" +
                         $"🛒 خریداران منتظر در صف (بهترین قیمت‌های خرید):\n" +
                         $"تعداد واحد | قیمت پیشنهادی هر واحد\n" +
                         $"------------------------------\n";

            if (buyOrders.Count == 0)
            {
                msg += "• (صف خرید خالی است)\n";
            }
            else
            {
                var groupedBuys = buyOrders
                    .GroupBy(o => o.Price)
                    .Select(g => new
                    {
                        Price = g.Key,
                        TotalQty = g.Sum(o => o.Quantity),
                        Count = g.Count(),
                        FirstUser = g.First().UserId
                    })
                    .OrderByDescending(x => x.Price)
                    .Take(5)
                    .ToList();

                for (int i = 0; i < groupedBuys.Count; i++)
                {
                    var item = groupedBuys[i];
                    msg += $"• {item.TotalQty:N0} واحد ─── هر واحد {FmtPrice(item.Price)}\n";
                }
            }

            msg += $"━━━━━━━━━━━━━━━━━━━━━━\n" +
                   $"💰 فروشندگان منتظر در صف (بهترین قیمت‌های فروش):\n" +
                   $"تعداد واحد | قیمت پیشنهادی هر واحد\n" +
                   $"------------------------------\n";

            if (sellOrders.Count == 0)
            {
                msg += "• (صف فروش خالی است)\n";
            }
            else
            {
                var groupedSells = sellOrders
                    .GroupBy(o => o.Price)
                    .Select(g => new
                    {
                        Price = g.Key,
                        TotalQty = g.Sum(o => o.Quantity),
                        Count = g.Count(),
                        FirstUser = g.First().UserId
                    })
                    .OrderBy(x => x.Price)
                    .Take(5)
                    .ToList();

                for (int i = 0; i < groupedSells.Count; i++)
                {
                    var item = groupedSells[i];
                    string uLabel = item.FirstUser == 0 ? "  (خزانه مرکزی)" : "";
                    msg += $"• {item.TotalQty:N0} واحد ─── هر واحد {FmtPrice(item.Price)}{uLabel}\n";
                }
            }

            msg += $"━━━━━━━━━━━━━━━━━━━━━━\n" +
                   $"💡 راهنمای ساده تابلو:\n" +
                   $"• خرید فوری ➔ سیستم خودکار از ارزان‌ترین فروشنده برایتان می‌خرد.\n" +
                   $"• فروش فوری ➔ سیستم خودکار به بالاترین خریدار می‌فروشد.\n" +
                   $"• اگر قیمت خاصی مدنظرتان است ➔ دکمه «سفارش در قیمت دلخواه» را بزنید تا سفارشتان وارد صف شود!";

            var kb = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("🛒 سفارش خرید در قیمت دلخواه", $"LIMIT_BUY_INPUT_{symbol}"),
                    InlineKeyboardButton.WithCallbackData("💰 سفارش فروش در قیمت دلخواه", $"LIMIT_SELL_INPUT_{symbol}")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("🛒 خرید فوری (ارزان‌ترین فروشنده)", $"BUY_MENU_{symbol}"),
                    InlineKeyboardButton.WithCallbackData("💰 فروش فوری (بالاترین خریدار)", $"SELL_MENU_{symbol}")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("📋 سفارش‌های من (لغو)", $"MY_ORDERS_{symbol}"),
                    InlineKeyboardButton.WithCallbackData("🔄 به‌روزرسانی تابلو", $"BOARD_{symbol}")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("🔙 بازگشت به صفحه ارز", $"VIEW_SYMBOL_{symbol}")
                }
            });

            await bot.SendMessage(chatId, msg, replyMarkup: kb, cancellationToken: ct);
        }

        private static async Task SendUserOpenOrdersAsync(ITelegramBotClient bot, long chatId, long userId, string symbol, CancellationToken ct)
        {
            List<Order> userOrders = new();
            lock (_dataLock)
            {
                if (Market.TryGetValue(symbol, out var c))
                {
                    userOrders = c.Orders.Where(o => o.UserId == userId).ToList();
                }
            }

            if (userOrders.Count == 0)
            {
                await bot.SendMessage(chatId, $"📋 شما هیچ سفارش بازی در تابلوی معاملاتی {symbol} ندارید.", cancellationToken: ct);
                return;
            }

            string msg = $"📋 سفارش‌های باز شما در تابلوی معاملاتی {symbol}:\n\n" +
                         $"برای لغو هر سفارش و بازگشت وجه/سهام مسدودشده، روی دکمه لغو مربوطه کلیک کنید:\n";

            var rows = new List<InlineKeyboardButton[]>();
            foreach (var o in userOrders)
            {
                string typeName = o.Type == "BUY" ? "خرید 🛒" : "فروش 💰";
                msg += $"• [{typeName}] {o.Quantity:N0} واحد به قیمت {FmtPrice(o.Price)}\n";
                rows.Add(new[] { InlineKeyboardButton.WithCallbackData($"❌ لغو سفارش {typeName} ({o.Quantity} واحد @ {FmtPrice(o.Price)})", $"CANCEL_ORDER_{symbol}_{o.Timestamp.Ticks}") });
            }

            rows.Add(new[] { InlineKeyboardButton.WithCallbackData("🔙 بازگشت به تابلو", $"BOARD_{symbol}") });

            await bot.SendMessage(chatId, msg, replyMarkup: new InlineKeyboardMarkup(rows), cancellationToken: ct);
        }

        private static async Task SendUserPortfolioAsync(ITelegramBotClient bot, long chatId, long userId, CancellationToken ct)
        {
            User? uCopy = null;
            var holdings = new List<(string Symbol, long Qty, decimal Price, decimal Value, decimal CostBasis, decimal PnL, decimal PnLPct)>();
            decimal balance = 0m;
            decimal totalStockVal = 0m;
            decimal totalCostBasisVal = 0m;

            lock (_dataLock)
            {
                if (Users.TryGetValue(userId, out var u) && u != null)
                {
                    uCopy = u;
                    balance = u.Balance;
                    foreach (var h in u.Portfolio.Where(x => x.Value > 0))
                    {
                        decimal curPrice = GetCurrentPrice(h.Key);
                        decimal val = curPrice * h.Value;
                        decimal basis = u.CostBasis.TryGetValue(h.Key, out var cb) ? cb : curPrice;
                        decimal costVal = basis * h.Value;
                        decimal pnl = val - costVal;
                        decimal pnlPct = basis > 0 ? ((curPrice - basis) / basis) * 100m : 0m;

                        holdings.Add((h.Key, h.Value, curPrice, val, basis, pnl, pnlPct));
                        totalStockVal += val;
                        totalCostBasisVal += costVal;
                    }
                }
            }

            if (uCopy == null) return;

            decimal netWorth = balance + totalStockVal;
            decimal totalUnrealizedPnL = totalStockVal - totalCostBasisVal;
            decimal totalUnrealizedPct = totalCostBasisVal > 0 ? (totalUnrealizedPnL / totalCostBasisVal) * 100m : 0m;
            string totalPnLIcon = totalUnrealizedPnL >= 0 ? "🟢" : "🔴";
            string totalPnLSign = totalUnrealizedPnL >= 0 ? "+" : "";

            string msg = $"💼 پرتفو و سبد دارایی اختصاصی — @{uCopy.Username}\n\n" +
                         $"💵 موجودی نقدی دلار (Cash): {FmtMoney(balance)}\n" +
                         $"💎 مجموع ارزش سهام‌ها (Stock): {FmtMoney(totalStockVal)}\n" +
                         $"🏆 ارزش کل دارایی حساب (Net Worth): {FmtMoney(netWorth)}\n" +
                         $"{totalPnLIcon} سود/زیان باز کل سبد: {totalPnLSign}{FmtMoney(totalUnrealizedPnL)} ({totalPnLSign}{totalUnrealizedPct:N1}%)\n\n" +
                         $"━━━━━━━━━━━━━━━━━━━━━━\n";

            if (holdings.Count == 0)
            {
                msg += "🔹 شما در حال حاضر هیچ سهامی در سبد دارایی خود ندارید.\n" +
                       "💡 برای شروع معاملات و خرید سهام، روی دکمه‌های زیر کلیک کنید:\n" +
                       $"━━━━━━━━━━━━━━━━━━━━━━";
            }
            else
            {
                msg += "📦 تفکیک سهام‌های خریداری‌شده در سبد:\n\n";
                foreach (var h in holdings.OrderByDescending(x => x.Value))
                {
                    decimal sharePct = totalStockVal > 0 ? (h.Value / totalStockVal) * 100m : 0m;
                    string pnlIcon = h.PnL >= 0 ? "🟢" : "🔴";
                    string pnlSign = h.PnL >= 0 ? "+" : "";
                    msg += $"┌ 🏷 نماد: {h.Symbol} — (سهم از سبد سهام: {sharePct:N1}%)\n" +
                           $"├ 📦 موجودی سهام: {h.Qty:N0} واحد\n" +
                           $"├ 💵 قیمت لحظه‌ای بازار: {FmtPrice(h.Price)}\n" +
                           $"├ 🎯 نقطه سر به سر شما (با کارمزد): {FmtPrice(h.CostBasis)}\n" +
                           $"├ 💎 ارزش کل این سهم: {FmtMoney(h.Value)}\n" +
                           $"└ {pnlIcon} سود/زیان این سهم: {pnlSign}{FmtMoney(h.PnL)} ({pnlSign}{h.PnLPct:N1}%)\n\n";
                }
                msg += $"━━━━━━━━━━━━━━━━━━━━━━\n" +
                       $"💡 برای فروش در تابلو P2P یا مشاهده تابلوی معاملاتی هر سهم، روی دکمه مربوطه کلیک کنید:";
            }

            var rows = new List<InlineKeyboardButton[]>();
            foreach (var h in holdings.Take(5))
            {
                rows.Add(new[]
                {
                    InlineKeyboardButton.WithCallbackData($"💰 فروش در تابلو {h.Symbol}", $"LIMIT_SELL_INPUT_{h.Symbol}"),
                    InlineKeyboardButton.WithCallbackData($"📋 تابلوی {h.Symbol}", $"BOARD_{h.Symbol}")
                });
            }

            rows.Add(new[]
            {
                InlineKeyboardButton.WithCallbackData("📊 بازار و قیمت‌ها", "VIEW_MARKET"),
                InlineKeyboardButton.WithCallbackData("🔄 به‌روزرسانی پرتفو", "REFRESH_PORTFOLIO")
            });

            await bot.SendMessage(chatId, msg, replyMarkup: new InlineKeyboardMarkup(rows), cancellationToken: ct);
        }

        private static async Task SendSymbolCardAsync(ITelegramBotClient bot, long chatId, string symbol, CancellationToken ct)
        {
            Currency? cCopy = null;
            decimal currentPrice = 0m;
            int openSells = 0;
            int openBuys = 0;

            lock (_dataLock)
            {
                if (!Market.TryGetValue(symbol, out var c))
                {
                    cCopy = null;
                }
                else
                {
                    cCopy = c;
                    currentPrice = GetCurrentPrice(symbol);
                    openSells = c.Orders.Count(o => o.Type == "SELL");
                    openBuys = c.Orders.Count(o => o.Type == "BUY");
                }
            }

            if (cCopy == null)
            {
                await bot.SendMessage(chatId, $"❌ ارز {symbol} یافت نشد.", cancellationToken: ct);
                return;
            }

            decimal baseVal = cCopy.BaseValue;
            decimal changePct = baseVal > 0 ? ((currentPrice - baseVal) / baseVal) * 100m : 0m;

            string caption = $"🏷 ارز: {cCopy.Symbol} — {cCopy.Description}\n\n" +
                             $"💵 قیمت لحظه‌ای: {FmtPrice(currentPrice)} ({(changePct >= 0 ? "+" : "")}{changePct:N1}% نسبت به قیمت پایه)\n" +
                             $"💎 قیمت پایه: {FmtPrice(baseVal)}\n" +
                             $"📦 عرضه در گردش: {cCopy.CirculatingSupply:N0} از {cCopy.TotalSupply:N0} واحد\n" +
                             $"📋 سفارشات فعال بازار: {openSells} فروش | {openBuys} خرید\n\n" +
                             $"💡 برای خرید، فروش فوری یا مشاهده نمودار، روی دکمه‌های زیر کلیک کنید:";

            var inlineKb = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("🛒 خرید فوری (از خزانه / تابلو)", $"BUY_MENU_{symbol}"),
                    InlineKeyboardButton.WithCallbackData("💰 ثبت سفارش فروش (در تابلو)", $"LIMIT_SELL_INPUT_{symbol}")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("📋 تابلوی معاملاتی لایو", $"BOARD_{symbol}")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("📈 چارت گرافیکی", $"CHART_{symbol}"),
                    InlineKeyboardButton.WithCallbackData("🔄 به‌روزرسانی", $"VIEW_SYMBOL_{symbol}")
                }
            });

            bool photoSent = false;
            if (!string.IsNullOrWhiteSpace(cCopy.PhotoUrl))
            {
                try
                {
                    await bot.SendPhoto(
                        chatId,
                        InputFile.FromString(cCopy.PhotoUrl),
                        caption: caption,
                        replyMarkup: inlineKb,
                        cancellationToken: ct
                    );
                    photoSent = true;
                }
                catch
                {
                    photoSent = false;
                }
            }

            if (!photoSent)
            {
                await bot.SendMessage(
                    chatId,
                    caption,
                    replyMarkup: inlineKb,
                    cancellationToken: ct
                );
            }
        }

        private static async Task SendMarketOverviewAsync(ITelegramBotClient bot, long chatId, CancellationToken ct)
        {
            string msg = "📊 بازار لحظه‌ای ارزها و سهام:\n\n";
            List<InlineKeyboardButton> symbolButtons;
            lock (_dataLock)
            {
                foreach (var c in Market.Values)
                {
                    decimal price = GetCurrentPrice(c.Symbol);
                    decimal changePct = c.BaseValue > 0 ? ((price - c.BaseValue) / c.BaseValue) * 100m : 0m;
                    string arrow = changePct >= 0 ? "🟢" : "🔴";
                    msg += $"{arrow} {c.Symbol} | قیمت: {FmtPrice(price)} ({(changePct >= 0 ? "+" : "")}{changePct:N1}%)\n";
                }
                symbolButtons = Market.Keys.Select(sym =>
                    InlineKeyboardButton.WithCallbackData($"📊 {sym} ({FmtPrice(GetCurrentPrice(sym))})", $"VIEW_SYMBOL_{sym}")
                ).ToList();
            }

            msg += "\n💡 برای مشاهده کارت اختصاصی هر ارز، تصویر و معامله فوری، روی نماد مورد نظر کلیک کنید:";

            var rows = new List<InlineKeyboardButton[]>();
            for (int i = 0; i < symbolButtons.Count; i += 2)
            {
                if (i + 1 < symbolButtons.Count)
                    rows.Add(new[] { symbolButtons[i], symbolButtons[i + 1] });
                else
                    rows.Add(new[] { symbolButtons[i] });
            }

            await bot.SendMessage(chatId, msg, replyMarkup: new InlineKeyboardMarkup(rows), cancellationToken: ct);
        }

        private static async Task SendBuyMenuSelectorAsync(ITelegramBotClient bot, long chatId, CancellationToken ct)
        {
            string msg = "🛒 منوی انتخاب ارز برای خرید سهام:\n\n" +
                         "ارز مورد نظر را از لیست زیر انتخاب کنید یا از دستور متنی زیر استفاده کنید:\n" +
                         "مثال: خرید BTC 10 65000";

            List<InlineKeyboardButton> symbolButtons;
            lock (_dataLock)
            {
                symbolButtons = Market.Keys.Select(sym =>
                    InlineKeyboardButton.WithCallbackData($"🛒 خرید {sym} ({FmtPrice(GetCurrentPrice(sym))})", $"BUY_MENU_{sym}")
                ).ToList();
            }

            var rows = new List<InlineKeyboardButton[]>();
            for (int i = 0; i < symbolButtons.Count; i += 2)
            {
                if (i + 1 < symbolButtons.Count)
                    rows.Add(new[] { symbolButtons[i], symbolButtons[i + 1] });
                else
                    rows.Add(new[] { symbolButtons[i] });
            }

            await bot.SendMessage(chatId, msg, replyMarkup: new InlineKeyboardMarkup(rows), cancellationToken: ct);
        }

        private static async Task SendSellMenuSelectorAsync(ITelegramBotClient bot, long chatId, CancellationToken ct)
        {
            string msg = "💰 منوی انتخاب ارز برای ثبت سفارش فروش در تابلوی معاملاتی (بورس همتا به همتا):\n\n" +
                         "سفارش فروش شما در تابلوی بورس (صف فروش) ثبت شده و توسط خریداران واقعی معامله می‌شود.\n" +
                         "لطفاً ارز مورد نظر را انتخاب کنید:";

            List<InlineKeyboardButton> symbolButtons;
            lock (_dataLock)
            {
                symbolButtons = Market.Keys.Select(sym =>
                    InlineKeyboardButton.WithCallbackData($"💰 فروش در تابلو {sym}", $"LIMIT_SELL_INPUT_{sym}")
                ).ToList();
            }

            var rows = new List<InlineKeyboardButton[]>();
            for (int i = 0; i < symbolButtons.Count; i += 2)
            {
                if (i + 1 < symbolButtons.Count)
                    rows.Add(new[] { symbolButtons[i], symbolButtons[i + 1] });
                else
                    rows.Add(new[] { symbolButtons[i] });
            }

            await bot.SendMessage(chatId, msg, replyMarkup: new InlineKeyboardMarkup(rows), cancellationToken: ct);
        }

        private static async Task SendChartSelectorAsync(ITelegramBotClient bot, long chatId, CancellationToken ct)
        {
            string msg = "📈 منوی انتخاب چارت و نمودار گرافیکی روند قیمت:\n\n" +
                         "برای تولید چارت گرافیکی (ScottPlot)، روی نماد مورد نظر کلیک کنید:";

            List<InlineKeyboardButton> symbolButtons;
            lock (_dataLock)
            {
                symbolButtons = Market.Keys.Select(sym =>
                    InlineKeyboardButton.WithCallbackData($"📈 چارت {sym}", $"CHART_{sym}")
                ).ToList();
            }

            var rows = new List<InlineKeyboardButton[]>();
            for (int i = 0; i < symbolButtons.Count; i += 2)
            {
                if (i + 1 < symbolButtons.Count)
                    rows.Add(new[] { symbolButtons[i], symbolButtons[i + 1] });
                else
                    rows.Add(new[] { symbolButtons[i] });
            }

            await bot.SendMessage(chatId, msg, replyMarkup: new InlineKeyboardMarkup(rows), cancellationToken: ct);
        }

        private static Task SendGraphicChartAsync(ITelegramBotClient bot, long chatId, string symbol, CancellationToken ct)
        {
            return SendGraphicChartAsync(bot, chatId, symbol, "ALL", ct);
        }

        private static InlineKeyboardMarkup GetChartTimeframeKeyboard(string symbol)
        {
            double ageHours = 0;
            lock (_dataLock)
            {
                if (Market.TryGetValue(symbol, out var c))
                {
                    if (c.CreatedAt == DateTime.MinValue) c.CreatedAt = DateTime.UtcNow.AddDays(-14);
                    ageHours = (DateTime.UtcNow - c.CreatedAt).TotalHours;
                }
            }

            var buttons = new List<InlineKeyboardButton>();

            // ۳ ساعت اخیر همیشه نمایش داده می‌شود
            buttons.Add(InlineKeyboardButton.WithCallbackData("۳ ساعت اخیر", $"CHART_TF_{symbol}_3H"));

            // نمایش تا حداکثر عمر واقعی ارز
            if (ageHours >= 3.0)
                buttons.Add(InlineKeyboardButton.WithCallbackData("۱۲ ساعت اخیر", $"CHART_TF_{symbol}_12H"));

            if (ageHours >= 12.0)
                buttons.Add(InlineKeyboardButton.WithCallbackData("۲۴ ساعت اخیر", $"CHART_TF_{symbol}_24H"));

            if (ageHours >= 24.0)
                buttons.Add(InlineKeyboardButton.WithCallbackData("۳ روز اخیر", $"CHART_TF_{symbol}_3D"));

            if (ageHours >= 72.0)
                buttons.Add(InlineKeyboardButton.WithCallbackData("۷ روز اخیر", $"CHART_TF_{symbol}_7D"));

            if (ageHours >= 168.0)
                buttons.Add(InlineKeyboardButton.WithCallbackData("۳۰ روز اخیر", $"CHART_TF_{symbol}_30D"));

            buttons.Add(InlineKeyboardButton.WithCallbackData("کل تاریخچه", $"CHART_TF_{symbol}_ALL"));

            var rows = new List<InlineKeyboardButton[]>();
            for (int i = 0; i < buttons.Count; i += 2)
            {
                if (i + 1 < buttons.Count)
                    rows.Add(new[] { buttons[i], buttons[i + 1] });
                else
                    rows.Add(new[] { buttons[i] });
            }

            rows.Add(new[] { InlineKeyboardButton.WithCallbackData("🔙 بازگشت به صفحه ارز", $"VIEW_SYMBOL_{symbol}") });

            return new InlineKeyboardMarkup(rows);
        }

        private static async Task SendGraphicChartAsync(ITelegramBotClient bot, long chatId, string symbol, string timeframe, CancellationToken ct)
        {
            DateTime[] timePoints;
            decimal[] prices;
            decimal baseValue;
            decimal curPrice;
            string tfTitle = "کل تاریخچه";
            DateTime minTime = DateTime.MinValue;

            switch (timeframe.ToUpper())
            {
                case "3H": minTime = DateTime.UtcNow.AddHours(-3); tfTitle = "۳ ساعت اخیر"; break;
                case "12H": minTime = DateTime.UtcNow.AddHours(-12); tfTitle = "۱۲ ساعت اخیر"; break;
                case "24H": minTime = DateTime.UtcNow.AddHours(-24); tfTitle = "۲۴ ساعت اخیر"; break;
                case "3D": minTime = DateTime.UtcNow.AddDays(-3); tfTitle = "۳ روز اخیر"; break;
                case "7D": minTime = DateTime.UtcNow.AddDays(-7); tfTitle = "۷ روز اخیر"; break;
                case "30D": minTime = DateTime.UtcNow.AddDays(-30); tfTitle = "۳۰ روز اخیر"; break;
                default: minTime = DateTime.MinValue; tfTitle = "کل تاریخچه"; break;
            }

            lock (_dataLock)
            {
                if (!Market.TryGetValue(symbol, out var c))
                {
                    timePoints = Array.Empty<DateTime>();
                    prices = Array.Empty<decimal>();
                    baseValue = 1m;
                    curPrice = 1m;
                }
                else
                {
                    baseValue = c.BaseValue;
                    curPrice = GetCurrentPrice(symbol);
                    var subset = c.TimedPriceHistory.Where(p => p.Timestamp >= minTime).ToList();
                    if (subset.Count == 0)
                    {
                        subset = c.TimedPriceHistory.TakeLast(30).ToList();
                    }
                    if (subset.Count == 1)
                    {
                        subset.Insert(0, new PricePoint { Timestamp = subset[0].Timestamp.AddMinutes(-30), Price = c.BaseValue });
                    }
                    timePoints = subset.Select(p => p.Timestamp).ToArray();
                    prices = subset.Select(p => p.Price).ToArray();
                }
            }

            if (prices.Length == 0)
            {
                await bot.SendMessage(chatId, $"❌ ارز {symbol} یافت نشد.", cancellationToken: ct);
                return;
            }

            decimal startPrice = prices.First();
            decimal changePct = startPrice > 0 ? ((curPrice - startPrice) / startPrice) * 100m : 0m;
            string changeIcon = changePct >= 0 ? "🟢" : "🔴";
            string changeSign = changePct >= 0 ? "+" : "";
            string shamsiTime = GetShamsiTehranTime(DateTime.UtcNow);

            // توزیع زمانی نرم و سعودی برای نقاط تا هرگز روی هم (عمودی) انباشته نشوند و نمودار کاملاً کشیده و تمیز رسم شود
            DateTime endDt = DateTime.UtcNow;
            DateTime startDt = endDt.AddHours(-24);
            switch (timeframe.ToUpper())
            {
                case "3H": startDt = endDt.AddHours(-3); break;
                case "12H": startDt = endDt.AddHours(-12); break;
                case "24H": startDt = endDt.AddHours(-24); break;
                case "3D": startDt = endDt.AddDays(-3); break;
                case "7D": startDt = endDt.AddDays(-7); break;
                case "30D": startDt = endDt.AddDays(-30); break;
                default: startDt = endDt.AddDays(-14); break;
            }
            if (timePoints.Length > 1 && timePoints.First() < startDt)
                startDt = timePoints.First();

            double totalSeconds = Math.Max(1800, (endDt - startDt).TotalSeconds);
            double stepSec = totalSeconds / Math.Max(1, prices.Length - 1);
            for (int i = 0; i < timePoints.Length; i++)
            {
                timePoints[i] = startDt.AddSeconds(i * stepSec);
            }

            try
            {
                double[] xs = timePoints.Select(dt => dt.ToOADate()).ToArray();
                double[] ys = prices.Select(p => (double)p).ToArray();

                var plt = new Plot();
                var scatter = plt.Add.Scatter(xs, ys);
                scatter.LineWidth = 2.5f;
                scatter.MarkerSize = prices.Length > 35 ? 3 : 6;
                scatter.Color = ScottPlot.Color.FromHex("#1f77b4");
                plt.Grid.MajorLineColor = ScottPlot.Colors.LightGray.WithAlpha(0.35);

                plt.Axes.DateTimeTicksBottom(); // نمایش ساعت و تاریخ واقعی بدون هم‌پوشانی
                plt.Title($"{symbol} / USD — {timeframe.ToUpper()} Timeframe");
                plt.XLabel("Time (Tehran Time — HH:mm / Date)");
                plt.YLabel("Price ($ USD)");
                plt.Axes.Left.Label.Text = "Price ($)";

                string filePath = $"{symbol}_chart_{DateTime.UtcNow.Ticks}.png";
                await Task.Run(() => plt.SavePng(filePath, 800, 400), ct);

                await using var stream = IOFile.OpenRead(filePath);
                string caption = $"📊 نمودار قیمت **{symbol}** — *(بازه: {tfTitle})*\n" +
                                 $"📅 تاریخ و زمان: **{shamsiTime}**\n\n" +
                                 $"💵 قیمت لحظه‌ای بازار: **{FmtPrice(curPrice)}**\n" +
                                 $"💎 قیمت پایه اولیه: {FmtPrice(baseValue)}\n" +
                                 $"📈 تغییرات این بازه زمانی: **{changeSign}{changePct:N1}%** {changeIcon}\n\n" +
                                 $"💡 برای تغییر بازه زمانی نمودار، روی دکمه‌های زیر کلیک کنید:";

                await bot.SendPhoto(
                    chatId,
                    InputFile.FromStream(stream, filePath),
                    caption: caption,
                    parseMode: ParseMode.Markdown,
                    replyMarkup: GetChartTimeframeKeyboard(symbol),
                    cancellationToken: ct
                );
                try { IOFile.Delete(filePath); } catch { }
            }
            catch
            {
                await bot.SendMessage(chatId, $"📉 تاریخچه قیمت‌های {symbol} ($) — بازه: {tfTitle}\n" + string.Join(" → ", prices.Select(p => FmtPrice(p))), replyMarkup: GetChartTimeframeKeyboard(symbol), cancellationToken: ct);
            }
        }

        private static async Task SendLeaderboardAsync(ITelegramBotClient bot, long chatId, long currentUserId, CancellationToken ct)
        {
            List<User> leaderboard;
            lock (_dataLock)
            {
                leaderboard = Users.Values
                    .Where(u => u.UserId != 0)
                    .OrderByDescending(u => u.Balance + u.Portfolio.Sum(p => p.Value * GetCurrentPrice(p.Key)))
                    .Take(10)
                    .ToList();
            }

            string lb = "🏆 لیدربورد ۱۰ معامله‌گر برتر بازار (ارزش کل حساب):\n\n";
            for (int i = 0; i < leaderboard.Count; i++)
            {
                var u = leaderboard[i];
                decimal net;
                lock (_dataLock)
                {
                    net = u.Balance + u.Portfolio.Sum(p => p.Value * GetCurrentPrice(p.Key));
                }
                string rank = (i == 0) ? "🥇" : (i == 1) ? "🥈" : (i == 2) ? "🥉" : $"{i + 1}.";
                lb += $"{rank} @{u.Username} — {FmtMoney(net)} (نقد: {FmtMoney(u.Balance)})\n";
            }

            await bot.SendMessage(
                chatId,
                lb,
                replyMarkup: currentUserId == OwnerId ? GetOwnerKeyboard() : GetUserKeyboard(currentUserId),
                cancellationToken: ct
            );
        }

        private static ReplyKeyboardMarkup GetUserKeyboard(long userId)
        {
            var rows = new List<KeyboardButton[]>
            {
                new KeyboardButton[] { "📊 بازار و قیمت‌ها", "📋 تابلوی معاملاتی", "💼 پرتفو من" },
                new KeyboardButton[] { "🛒 خرید سهام", "💰 فروش سهام", "📈 نمودار و چارت" },
                new KeyboardButton[] { "🏆 لیدربورد برترین‌ها", "🎁 دعوت دوستان", "📖 راهنمای بات" }
            };

            if (userId == OwnerId)
            {
                rows.Add(new KeyboardButton[] { "👑 پنل مدیریت" });
            }

            return new ReplyKeyboardMarkup(rows) { ResizeKeyboard = true };
        }

        private static ReplyKeyboardMarkup GetOwnerKeyboard()
        {
            return new ReplyKeyboardMarkup(new[]
            {
                new KeyboardButton[] { "➕ اضافه کردن ارز", "📊 مرور بازار", "💰 موجودی کاربران" },
                new KeyboardButton[] { "🎮 کنترل پنل دارایی پلیر", "🎁 واریز / مدیریت سهام", "🔄 ریست دارایی کاربر" },
                new KeyboardButton[] { "🏦 تزریق نقدینگی", "🖼 تنظیم عکس ارز", "📝 تنظیم توضیحات ارز" },
                new KeyboardButton[] { "🗑 حذف ارز", "📈 تنظیم قیمت دستی", "📋 سفارشات باز" },
                new KeyboardButton[] { "🔄 ریست بازار", "🎲 رویداد تصادفی", "📰 رویدادهای ویژه" },
                new KeyboardButton[] { "🏆 لیدربورد", "🔙 بازگشت به منوی اصلی" }
            }) { ResizeKeyboard = true };
        }

        private static string GetCompleteHelpText()
        {
            return @"📖 راهنمای استفاده از بات معاملاتی نائومی (Naomi)

🌟 به بات نائومی خوش آمدید! تمامی معاملات، قیمت‌ها و موجودی‌ها بر حسب دلار ($) محاسبه می‌شود.

---
🔘 دکمه‌های منوی پایین صفحه:
• 📊 بازار و قیمت‌ها: مشاهده لیست قیمت‌های لحظه‌ای بازار و ورود به صفحه اختصاصی هر ارز همراه با عکس
• 📋 تابلوی معاملاتی: مشاهده تابلوی معاملاتی لایو (بورس همتا به همتا - P2P) شامل صف خرید، صف فروش، حجم‌ها و سفارش‌گذاری با قیمت دلخواه (Limit)
• 💼 پرتفو من: مشاهده سبد دارایی‌ها و ارزش کل حساب ($)
• 💰 موجودی من: مشاهده موجودی نقدی دلار ($) و سطح کاربری (Level / XP)
• 🛒 خرید سهام و 💰 فروش سهام: منوی سریع خرید و فروش هر ارز
• 📈 نمودار و چارت: دریافت نمودار گرافیکی روند قیمت ارزها (ScottPlot)
• 🏆 لیدربورد برترین‌ها: مشاهده ۱۰ معامله‌گر برتر با بیشترین ارزش دارایی
• 🎁 دعوت دوستان: دریافت لینک دعوت و پاداش $500 برای هر دعوت

---
⌨️ دستورات متنی سریع (بدون اسلش):
• بازار — مشاهده بازار و قیمت‌ها
• تابلو BTC — مشاهده تابلوی معاملاتی لایو بیت‌کوین
• خرید BTC 10 65000 — خرید ۱۰ واحد بیت‌کوین به قیمت واحد ۶۵۰۰۰ دلار
• فروش BTC 5 65000 — فروش ۵ واحد بیت‌کوین به قیمت واحد ۶۵۰۰۰ دلار
• چارت BTC — دریافت نمودار گرافیکی بیت‌کوین
• پنل ارز BTC — مشاهده اطلاعات کامل، تصویر و سفارشات باز
• پرتفو — مشاهده دارایی‌ها
• موجودی — مشاهده موجودی دلار

---
💡 سازوکار بورس همتا به همتا (P2P):
۱. 🏦 عرضه اولیه خزانه: خریدها تا زمانی که سهام اولیه خزانه دست مردم نیفتاده باشد به صورت آنی از خزانه انجام می‌شود. پس از اتمام سهام خزانه، خرید فقط همتا به همتا (P2P) خواهد بود!
۲. 🤝 فروش سهام همیشه ۱۰۰٪ همتا به همتا است: سفارش فروش شما در تابلوی معاملاتی (صف فروش) ثبت می‌شود و توسط خریداران واقعی معامله خواهد شد.
۳. 🌙 پاداش شبانه: هر شب ساعت ۱۲ (به وقت تهران)، اگر موجودی نقدی شما کمتر از $5,000 باشد، مبلغ $1,000 هدیه به حسابتان واریز می‌شود!";
        }

        // ===================== MATCHING ENGINE =====================
        private static void MatchOrders(string symbol, long triggeredByUserId)
        {
            EnsureInitialLiquidity(symbol);
            if (!Market.TryGetValue(symbol, out var currency)) return;
            var buys = currency.Orders.Where(o => o.Type == "BUY" && o.Quantity > 0).OrderByDescending(o => o.Price).ToList();
            var sells = currency.Orders.Where(o => o.Type == "SELL" && o.Quantity > 0).OrderBy(o => o.Price).ToList();

            foreach (var buy in buys)
            {
                foreach (var sell in sells)
                {
                    if (buy.Quantity <= 0 || sell.Quantity <= 0) continue;
                    if (buy.Price >= sell.Price || (sell.UserId == 0 && buy.Price >= sell.Price * 0.98m) || (buy.UserId == 0 && sell.Price <= buy.Price * 1.02m))
                    {
                        var matchQty = Math.Min(buy.Quantity, sell.Quantity);
                        var tradePrice = sell.Price;

                        if (!Users.ContainsKey(buy.UserId)) Users[buy.UserId] = new User { UserId = buy.UserId, Username = "unknown" };
                        if (!Users.ContainsKey(sell.UserId)) Users[sell.UserId] = new User { UserId = sell.UserId, Username = "unknown" };

                        var buyer = Users[buy.UserId];
                        var seller = Users[sell.UserId];

                        bool buyerCanPay = (buy.UserId == 0 || buyer.Balance >= tradePrice * matchQty);
                        bool sellerHasStock = (sell.UserId == 0 || (seller.Portfolio.TryGetValue(symbol, out var sq) && sq >= matchQty));

                        if (buyerCanPay && sellerHasStock)
                        {
                            decimal tax = tradePrice * matchQty * 0.01m; // ۱٪ کارمزد معامله

                            if (buy.UserId != 0)
                            {
                                decimal batchCost = (tradePrice * matchQty) + tax;
                                long oldQty = buyer.Portfolio.TryGetValue(symbol, out var bq) ? bq : 0;
                                decimal oldBasis = buyer.CostBasis.TryGetValue(symbol, out var cb) ? cb : tradePrice;
                                decimal oldTotalInvested = oldQty * oldBasis;

                                long newQty = oldQty + matchQty;
                                decimal newTotalInvested = oldTotalInvested + batchCost;
                                if (newQty > 0) buyer.CostBasis[symbol] = newTotalInvested / newQty; // نقطه سر به سر جدید با کارمزد

                                buyer.Balance -= batchCost;
                                if (!buyer.Portfolio.ContainsKey(symbol)) buyer.Portfolio[symbol] = 0;
                                buyer.Portfolio[symbol] = newQty;
                            }

                            if (sell.UserId != 0)
                            {
                                decimal netProceeds = (tradePrice * matchQty) - tax;
                                decimal basisPerUnit = seller.CostBasis.TryGetValue(symbol, out var cb) ? cb : tradePrice;
                                decimal realizedPnL = netProceeds - (basisPerUnit * matchQty);

                                seller.TotalProfit += realizedPnL;
                                seller.Balance += netProceeds;
                                if (!seller.Portfolio.ContainsKey(symbol)) seller.Portfolio[symbol] = 0;
                                seller.Portfolio[symbol] -= matchQty;
                                if (seller.Portfolio[symbol] <= 0)
                                {
                                    seller.Portfolio.Remove(symbol);
                                    seller.CostBasis.Remove(symbol);
                                }
                            }

                            buy.Quantity -= matchQty;
                            sell.Quantity -= matchQty;

                            // موتور پیشرفته الاستیک قیمت‌گذاری ارز بر اساس حجم دلاری معامله و ارزش کل بازار (Advanced Elastic AMM & Bonding Curve)
                            decimal tradeVolume = tradePrice * matchQty;
                            decimal marketCap = Math.Max(10000m, currency.CirculatingSupply * tradePrice);
                            decimal volumeRatio = tradeVolume / marketCap;
                            decimal elasticity = Math.Max(0.06m, Math.Min(0.40m, 50000m / marketCap));
                            decimal impactPct = Math.Min(0.15m, Math.Max(0.0002m, (decimal)Math.Sqrt((double)volumeRatio) * elasticity));

                            decimal newTradePrice = tradePrice;
                            if (buy.UserId != 0 && sell.UserId == 0) // تقاضای خرید از خزانه -> ۱۰۰٪ افزایش قیمت بر اساس الاستیسیته
                            {
                                newTradePrice = Math.Max(0.01m, tradePrice * (1m + impactPct));
                            }
                            else if (buy.UserId == 0 && sell.UserId != 0) // فشار فروش به خزانه -> ۱۰۰٪ کاهش قیمت
                            {
                                newTradePrice = Math.Max(0.01m, tradePrice * (1m - (impactPct * 0.85m)));
                            }
                            else // معامله P2P بین دو کاربر واقعی
                            {
                                if (buy.Price >= tradePrice * 1.001m)
                                    newTradePrice = Math.Max(0.01m, tradePrice * (1m + (impactPct * 0.6m)));
                                else if (sell.Price <= tradePrice * 0.999m)
                                    newTradePrice = Math.Max(0.01m, tradePrice * (1m - (impactPct * 0.6m)));
                                else
                                    newTradePrice = tradePrice;
                            }

                            currency.PriceHistory.Add(newTradePrice);
                            if (currency.PriceHistory.Count > 100) currency.PriceHistory.RemoveAt(0);

                            currency.TimedPriceHistory.Add(new PricePoint { Timestamp = DateTime.UtcNow, Price = newTradePrice });
                            if (currency.TimedPriceHistory.Count > 5000) currency.TimedPriceHistory.RemoveAt(0);

                            // به‌روزرسانی خودکار سفارشات خزانه با قیمت جدید بازار
                            AdjustTreasuryOrders(currency, newTradePrice);

                            if (buy.UserId != 0 && sell.UserId != 0)
                            {
                                UpdateUserStats(buyer, seller);
                                string buyMsg = $"🤝 سفارش شما در تابلوی معاملاتی انجام شد!\nشما {matchQty:N0} واحد {symbol} را به قیمت واحد {FmtPrice(tradePrice)} از تابلوی بورس خریداری کردید!\n💰 موجودی جدید دلار شما: {FmtMoney(buyer.Balance)}";
                                string sellMsg = $"🤝 سفارش شما در تابلوی معاملاتی انجام شد!\nتعداد {matchQty:N0} واحد از سهام {symbol} شما به قیمت واحد {FmtPrice(tradePrice)} در تابلوی بورس به فروش رسید!\n💰 مبلغ دریافتی با کسر کارمزد: {FmtMoney((tradePrice * matchQty) * 0.99m)}\n💵 موجودی جدید دلار شما: {FmtMoney(seller.Balance)}";

                                try { _ = Bot.SendMessage(buyer.UserId, buyMsg); } catch { }
                                try { _ = Bot.SendMessage(seller.UserId, sellMsg); } catch { }
                            }
                        }
                    }
                }
            }
            currency.Orders.RemoveAll(o => o.Quantity <= 0);
        }

        private static void AdjustTreasuryOrders(Currency currency, decimal newPrice)
        {
            foreach (var order in currency.Orders.Where(o => o.UserId == 0))
            {
                if (order.Type == "SELL")
                {
                    order.Price = newPrice;
                    if (order.Quantity < currency.CirculatingSupply / 10)
                        order.Quantity = Math.Max(1000, currency.TotalSupply / 10);
                }
                else if (order.Type == "BUY")
                {
                    order.Price = Math.Max(0.01m, newPrice * 0.95m);
                    if (order.Quantity < currency.CirculatingSupply / 10)
                        order.Quantity = Math.Max(1000, currency.TotalSupply / 10);
                }
            }
        }

        private static void UpdateUserStats(User buyer, User seller)
        {
            buyer.TotalTrades++;
            buyer.SuccessfulTrades++;
            buyer.XP += 10;
            if (buyer.XP >= buyer.Level * 100)
            {
                buyer.Level++;
                buyer.XP = 0;
            }

            seller.TotalTrades++;
            seller.SuccessfulTrades++;
            seller.XP += 10;
            if (seller.XP >= seller.Level * 100)
            {
                seller.Level++;
                seller.XP = 0;
            }
        }

        private static async Task DailyRewardScheduler()
        {
            while (true)
            {
                try
                {
                    var tehran = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Iran Standard Time"));
                    if (tehran.Hour == 0 && tehran.Minute == 0)
                    {
                        lock (_dataLock)
                        {
                            foreach (var user in Users.Values.Where(u => u.UserId != 0))
                            {
                                if (user.Balance < 5000m && (DateTime.UtcNow - user.LastDailyReward).TotalHours > 20)
                                {
                                    user.Balance += 1000m;
                                    user.LastDailyReward = DateTime.UtcNow;
                                    try
                                    {
                                        _ = Bot.SendMessage(user.UserId, $"🌙 پاداش شبانه: مبلغ {FmtMoney(1000m)} هدیه به حساب شما واریز شد!\n💰 موجودی جدید: {FmtMoney(user.Balance)}");
                                    }
                                    catch { }
                                }
                            }
                        }
                        RequestSave();
                    }
                }
                catch { }
                await Task.Delay(60000);
            }
        }

        private static async Task DatabaseBackupScheduler()
        {
            while (true)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromHours(6));
                    string backupFile = _useSqlite ? DbPath : JsonPath;
                    if (IOFile.Exists(backupFile))
                    {
                        await using var stream = IOFile.OpenRead(backupFile);
                        await Bot.SendDocument(
                            OwnerId,
                            InputFile.FromStream(stream, _useSqlite ? "naomi_data.db" : "naomi_data.json"),
                            caption: $"📦 بک‌آپ خودکار دیتابیس نائومی — {DateTime.Now:yyyy-MM-dd HH:mm}"
                        );
                    }
                }
                catch { }
            }
        }

        private static Task HandleErrorAsync(ITelegramBotClient bot, Exception exception, CancellationToken ct)
        {
            Console.WriteLine($"Telegram Error: {exception.Message}");
            return Task.CompletedTask;
        }
    }
}
