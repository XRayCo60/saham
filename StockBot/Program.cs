// StockBot.cs - Telegram Stock Market Bot v2 (No Slash Commands + Complete Interactive UI)
// تک‌فایل C# کامل - بدون هیچ دستوری که با / شروع شود
// پشتیبانی کامل از دکمه‌های شیشه‌ای، چارت گرافیکی ScottPlot، نمایش دلار ($)، نقدینگی اولیه و مدیریت تصویر ارزها

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

        public class Currency
        {
            public string Symbol { get; set; } = "";
            public long TotalSupply { get; set; }
            public decimal BaseValue { get; set; }
            public long CirculatingSupply { get; set; }
            public string PhotoUrl { get; set; } = "";
            public string Description { get; set; } = "";
            public List<Order> Orders { get; set; } = new();
            public List<decimal> PriceHistory { get; set; } = new();
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
        }

        private static Dictionary<string, Currency> Market = new();
        private static Dictionary<long, User> Users = new();
        private static Dictionary<long, string> UserStates = new();
        private static readonly string DataFile = "stockbot_data.json";

        public static string FmtMoney(decimal amount) => $"${amount:N2}";
        public static string FmtPrice(decimal price) => $"${price:N2}";

        public static async Task Main(string[] args)
        {
            LoadData();
            Bot = new TelegramBotClient(Token);
            var me = await Bot.GetMe();
            Console.WriteLine($"Bot started: @{me.Username}");

            _ = Task.Run(DailyRewardScheduler);
            _ = Task.Run(DatabaseBackupScheduler);

            var receiverOptions = new ReceiverOptions { AllowedUpdates = Array.Empty<UpdateType>() };
            Bot.StartReceiving(HandleUpdateAsync, HandleErrorAsync, receiverOptions);

            Console.WriteLine("Press any key to stop...");
            Console.ReadKey();
            SaveData();
        }

        private static void LoadData()
        {
            if (IOFile.Exists(DataFile))
            {
                try
                {
                    var json = IOFile.ReadAllText(DataFile);
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
                }
                catch { }
            }

            if (Market == null) Market = new();
            if (Users == null) Users = new();

            EnsureTreasuryAccount();
            EnsureDefaultCurrencies();
        }

        private static void SaveData()
        {
            try
            {
                var data = new { Market, Users };
                IOFile.WriteAllText(DataFile, JsonConvert.SerializeObject(data, Formatting.Indented));
            }
            catch { }
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
                    PhotoUrl = photoUrl
                };
                currency.PriceHistory.Add(baseValue);
                Market[symbol] = currency;
            }
        }

        private static void EnsureInitialLiquidity(string symbol)
        {
            if (!Market.TryGetValue(symbol, out var currency)) return;

            // ایجاد سفارش فروش اولیه خزانه در قیمت پایه
            if (!currency.Orders.Any(o => o.Type == "SELL"))
            {
                long ipoQty = currency.CirculatingSupply;
                if (ipoQty <= 0) ipoQty = Math.Max(1000, currency.TotalSupply / 10);
                currency.Orders.Add(new Order
                {
                    UserId = 0,
                    Type = "SELL",
                    Price = currency.BaseValue,
                    Quantity = ipoQty,
                    Timestamp = DateTime.UtcNow
                });
            }

            // ایجاد سفارش خرید تضمینی جهت نقدشوندگی فوری کاربران (۵٪ زیر قیمت پایه)
            if (!currency.Orders.Any(o => o.Type == "BUY"))
            {
                long buyQty = currency.CirculatingSupply;
                if (buyQty <= 0) buyQty = Math.Max(1000, currency.TotalSupply / 10);
                decimal buyPrice = Math.Max(0.01m, currency.BaseValue * 0.95m);
                currency.Orders.Add(new Order
                {
                    UserId = 0,
                    Type = "BUY",
                    Price = buyPrice,
                    Quantity = buyQty,
                    Timestamp = DateTime.UtcNow
                });
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
                    SaveData();
                    return;
                }

                if (update.Message is not { } message || message.From is null) return;
                var chatId = message.Chat.Id;
                var userId = message.From.Id;
                var text = message.Text?.Trim() ?? "";

                if (!Users.ContainsKey(userId))
                {
                    Users[userId] = new User
                    {
                        UserId = userId,
                        Username = message.From.Username ?? "unknown",
                        Balance = 5000m,
                        Level = 1,
                        XP = 0
                    };
                    Users[userId].DeviceFingerprints.Add(userId % 100000);
                    Users[userId].ReferralCode = "REF" + userId.ToString().Substring(Math.Max(0, userId.ToString().Length - 6));
                }
                else if (message.From.Username != null)
                {
                    Users[userId].Username = message.From.Username;
                }

                // مدیریت وضعیت‌های خاص (مانند ارسال عکس یا متن توسط ادمین برای تغییر مشخصات ارز)
                if (UserStates.TryGetValue(chatId, out var state))
                {
                    if (await HandleStateAsync(bot, message, state, ct))
                    {
                        SaveData();
                        return;
                    }
                }

                if (userId == OwnerId && IsOwnerCommand(text))
                    await HandleOwnerCommands(bot, message, text, ct);
                else
                    await HandleUserCommands(bot, message, text, ct);

                SaveData();
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
                "خبر مثبت", "خبر منفی", "هک", "جنگ", "رکود", "رشد ناگهانی", "سقوط آزاد", "بازگشت"
            };
            return ownerCmds.Contains(text) || text == "پنل" || text == "admin" || text == "👑 پنل مدیریت";
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
                if (Market.ContainsKey(text.ToUpper()))
                {
                    Market.Remove(text.ToUpper());
                    UserStates.Remove(chatId);
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
                if (decimal.TryParse(text, out var newPrice) && Market.ContainsKey(symbol))
                {
                    Market[symbol].PriceHistory.Add(newPrice);
                    UserStates.Remove(chatId);
                    await bot.SendMessage(chatId, $"✅ قیمت ارز {symbol} به {FmtPrice(newPrice)} تغییر یافت.", replyMarkup: GetOwnerKeyboard(), cancellationToken: ct);
                    return true;
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

                if (Market.ContainsKey(symbol) && !string.IsNullOrWhiteSpace(photoUrl))
                {
                    Market[symbol].PhotoUrl = photoUrl;
                    UserStates.Remove(chatId);
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
                if (Market.ContainsKey(symbol))
                {
                    Market[symbol].Description = text;
                    UserStates.Remove(chatId);
                    await bot.SendMessage(chatId, $"✅ توضیحات ارز {symbol} با موفقیت به‌روزرسانی شد!", replyMarkup: GetOwnerKeyboard(), cancellationToken: ct);
                    return true;
                }
            }

            return false;
        }

        // ===================== OWNER PANEL =====================
        private static async Task HandleOwnerCommands(ITelegramBotClient bot, Message message, string text, CancellationToken ct)
        {
            var chatId = message.Chat.Id;

            if (text == "پنل" || text == "admin" || text == "👑 پنل مدیریت")
            {
                await bot.SendMessage(
                    chatId,
                    "👑 به پنل مدیریت پیشرفته StockBot v2 خوش آمدید!",
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
                foreach (var c in Market.Values)
                {
                    decimal price = GetCurrentPrice(c.Symbol);
                    int sells = c.Orders.Count(o => o.Type == "SELL");
                    int buys = c.Orders.Count(o => o.Type == "BUY");
                    msg += $"🔸 {c.Symbol} | قیمت لحظه‌ای: {FmtPrice(price)} | قیمت پایه: {FmtPrice(c.BaseValue)}\n" +
                           $"   عرضه در گردش: {c.CirculatingSupply:N0} / {c.TotalSupply:N0}\n" +
                           $"   سفارشات باز: {sells} فروش | {buys} خرید\n\n";
                }
                await bot.SendMessage(chatId, msg, replyMarkup: GetOwnerKeyboard(), cancellationToken: ct);
            }
            else if (text == "💰 موجودی کاربران")
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

                string msg = $"👑 آمار کلی دارایی و موجودی کاربران:\n\n" +
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

                await bot.SendMessage(chatId, msg, replyMarkup: GetOwnerKeyboard(), cancellationToken: ct);
            }
            else if (text == "🏦 تزریق نقدینگی")
            {
                foreach (var symbol in Market.Keys.ToList())
                {
                    EnsureInitialLiquidity(symbol);
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
                await bot.SendMessage(chatId, msg, replyMarkup: GetOwnerKeyboard(), cancellationToken: ct);
            }
            else if (text == "🔄 ریست بازار")
            {
                Market.Clear();
                EnsureDefaultCurrencies();
                await bot.SendMessage(
                    chatId,
                    "⚠️ بازار کاملاً ریست شد و ارزهای اصلی (BTC, ETH, SOL, TON, DOGE) به همراه نقدینگی اولیه مجدداً ایجاد شدند!",
                    replyMarkup: GetOwnerKeyboard(),
                    cancellationToken: ct
                );
            }
            else if (text == "🎲 رویداد تصادفی")
            {
                if (Market.Count > 0)
                {
                    var random = new Random();
                    var symbol = Market.Keys.ElementAt(random.Next(Market.Count));
                    var currency = Market[symbol];
                    var change = random.Next(-30, 31);
                    var newPrice = Math.Max(0.01m, currency.PriceHistory.LastOrDefault(currency.BaseValue) * (1 + change / 100m));

                    currency.PriceHistory.Add(newPrice);

                    string eventMsg = $"🎲 رویداد تصادفی بازار!\nارز {symbol} با تغییر {change:+0;-0}% مواجه شد!\n💵 قیمت جدید: {FmtPrice(newPrice)}";
                    await bot.SendMessage(chatId, eventMsg, replyMarkup: GetOwnerKeyboard(), cancellationToken: ct);

                    foreach (var uid in Users.Keys.Where(id => id != 0))
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

                    string eventMsg = $"📰 رویداد ویژه بازار: {text}\nارز {symbol} با تغییر {change:+0;-0}% مواجه شد!\n💵 قیمت جدید: {FmtPrice(newPrice)}";
                    await bot.SendMessage(chatId, eventMsg, replyMarkup: GetOwnerKeyboard(), cancellationToken: ct);

                    foreach (var uid in Users.Keys.Where(id => id != 0))
                    {
                        try { await bot.SendMessage(uid, eventMsg); } catch { }
                    }
                }
            }
            else if (text == "🏆 لیدربورد")
            {
                await SendLeaderboardAsync(bot, chatId, OwnerId, ct);
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
            var userId = message.From.Id;
            var user = Users[userId];

            // مدیریت /start و کدهای رفرال
            if (text == "/start" || text.StartsWith("/start ") || text == "start" || text == "شروع")
            {
                if (text.StartsWith("/start "))
                {
                    var code = text.Split(' ')[1].Trim();
                    var referrer = Users.Values.FirstOrDefault(u => u.ReferralCode.Equals(code, StringComparison.OrdinalIgnoreCase));
                    if (referrer != null && referrer.UserId != userId && user.Referrals == 0 && user.TotalTrades == 0)
                    {
                        referrer.Balance += 500m;
                        referrer.Referrals++;
                        user.Balance += 200m;
                        try { await bot.SendMessage(referrer.UserId, $"🎉 تبریک! یک کاربر جدید با کد دعوت شما عضو شد! +$500.00 پاداش به موجودی شما اضافه شد."); } catch { }
                    }
                }

                string welcome = $"🌟 به شبیه‌ساز حرفه‌ای بازار سهام (StockBot v2) خوش آمدید!\n\n" +
                                 $"💰 موجودی دلار نقدی شما: {FmtMoney(user.Balance)}\n" +
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

            // پورتفولیو
            if (text == "پورتفولیو" || text == "💼 پورتفولیو من")
            {
                decimal totalStockVal = 0m;
                string p = $"💼 سبد دارایی و پورتفولیو شما (@{user.Username}):\n\n" +
                           $"💵 موجودی نقدی دلار: {FmtMoney(user.Balance)}\n\n" +
                           $"📦 سهام‌های خریداری‌شده:\n";

                bool hasStock = false;
                foreach (var h in user.Portfolio.Where(x => x.Value > 0))
                {
                    hasStock = true;
                    decimal curPrice = GetCurrentPrice(h.Key);
                    decimal val = curPrice * h.Value;
                    totalStockVal += val;
                    p += $"🔸 {h.Key}: {h.Value:N0} واحد (ارزش: {FmtMoney(val)} | قیمت واحد: {FmtPrice(curPrice)})\n";
                }

                if (!hasStock)
                {
                    p += "🔹 شما در حال حاضر هیچ سهامی در پورتفولیو ندارید.\n";
                }

                p += $"\n💎 مجموع ارزش سهام‌ها: {FmtMoney(totalStockVal)}\n" +
                     $"🏆 ارزش کل دارایی حساب (Net Worth): {FmtMoney(user.Balance + totalStockVal)}";

                await bot.SendMessage(chatId, p, replyMarkup: GetUserKeyboard(userId), cancellationToken: ct);
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
                                $"https://t.me/{(await bot.GetMe()).Username}?start={user.ReferralCode}\n\n" +
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
                    if (Market.TryGetValue(symbol, out var c))
                    {
                        string chart = $"📉 تاریخچه قیمت‌های {symbol} (۱۰ قیمت اخیر - $):\n";
                        chart += string.Join(" → ", c.PriceHistory.TakeLast(10).Select(p => FmtPrice(p)));
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
                    if (Market.TryGetValue(symbol, out var c))
                    {
                        if (user.Balance < price * qty)
                        {
                            await bot.SendMessage(chatId, $"❌ موجودی نقدی دلار شما کافی نیست.\nمبلغ مورد نیاز: {FmtMoney(price * qty)}\nموجودی شما: {FmtMoney(user.Balance)}", cancellationToken: ct);
                            return;
                        }

                        var order = new Order { UserId = userId, Type = "BUY", Price = price, Quantity = qty, Timestamp = DateTime.UtcNow };
                        c.Orders.Add(order);
                        MatchOrders(symbol, userId);
                        await bot.SendMessage(chatId, $"✅ سفارش خرید {qty:N0} واحد {symbol} به قیمت واحد {FmtPrice(price)} ثبت و بررسی شد!\n💰 موجودی دلار: {FmtMoney(Users[userId].Balance)}", replyMarkup: GetUserKeyboard(userId), cancellationToken: ct);
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
                    if (Market.TryGetValue(symbol, out var c))
                    {
                        var hasStock = user.Portfolio.TryGetValue(symbol, out var sq) ? sq : 0;
                        if (hasStock < qty)
                        {
                            await bot.SendMessage(chatId, $"❌ موجودی سهام شما از ارز {symbol} کافی نیست. موجودی: {hasStock:N0} واحد", cancellationToken: ct);
                            return;
                        }

                        var order = new Order { UserId = userId, Type = "SELL", Price = price, Quantity = qty, Timestamp = DateTime.UtcNow };
                        c.Orders.Add(order);
                        MatchOrders(symbol, userId);
                        await bot.SendMessage(chatId, $"✅ سفارش فروش {qty:N0} واحد {symbol} به قیمت واحد {FmtPrice(price)} ثبت و بررسی شد!\n💰 موجودی دلار: {FmtMoney(Users[userId].Balance)}", replyMarkup: GetUserKeyboard(userId), cancellationToken: ct);
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
                    var referrer = Users.Values.FirstOrDefault(u => u.ReferralCode.Equals(code, StringComparison.OrdinalIgnoreCase));
                    if (referrer != null && referrer.UserId != userId && user.Referrals == 0 && user.TotalTrades == 0)
                    {
                        referrer.Balance += 500m;
                        referrer.Referrals++;
                        user.Balance += 200m;
                        await bot.SendMessage(chatId, $"✅ با موفقیت با کد دعوت ثبت شدید! مبلغ {FmtMoney(200m)} به موجودی شما اضافه شد.", replyMarkup: GetUserKeyboard(userId), cancellationToken: ct);
                        try
                        {
                            await bot.SendMessage(referrer.UserId, $"🎉 یکی از دوستان شما با کد دعوتتان عضو شد! پاداش {FmtMoney(500m)} واریز شد.");
                        }
                        catch { }
                        return;
                    }
                    else
                    {
                        await bot.SendMessage(chatId, "❌ کد دعوت نامعتبر است یا قبلاً استفاده شده.", replyMarkup: GetUserKeyboard(userId), cancellationToken: ct);
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

            if (data.StartsWith("VIEW_SYMBOL_"))
            {
                var symbol = data.Split('_')[2];
                await SendSymbolCardAsync(bot, chatId, symbol, ct);
            }
            else if (data.StartsWith("CHART_"))
            {
                var symbol = data.Split('_')[1];
                await SendGraphicChartAsync(bot, chatId, symbol, ct);
            }
            else if (data.StartsWith("BUY_MENU_"))
            {
                var symbol = data.Split('_')[2];
                if (Market.TryGetValue(symbol, out var c))
                {
                    var price = GetCurrentPrice(symbol);
                    string msg = $"🛒 خرید سریع سهام {symbol} — قیمت لحظه‌ای واحد: {FmtPrice(price)}\n\n" +
                                 $"💵 موجودی دلار نقدی شما: {FmtMoney(Users[userId].Balance)}\n" +
                                 $"لطفاً مقدار مورد نظر برای خرید فوری در قیمت بازار را انتخاب کنید:";

                    var kb = new InlineKeyboardMarkup(new[]
                    {
                        new[]
                        {
                            InlineKeyboardButton.WithCallbackData("🛒 ۱۰ واحد", $"QUICK_BUY_{symbol}_10"),
                            InlineKeyboardButton.WithCallbackData("🛒 ۵۰ واحد", $"QUICK_BUY_{symbol}_50"),
                            InlineKeyboardButton.WithCallbackData("🛒 ۱۰۰ واحد", $"QUICK_BUY_{symbol}_100")
                        },
                        new[]
                        {
                            InlineKeyboardButton.WithCallbackData("🔙 بازگشت به پنل ارز", $"VIEW_SYMBOL_{symbol}")
                        }
                    });

                    await bot.SendMessage(chatId, msg, replyMarkup: kb, cancellationToken: ct);
                }
            }
            else if (data.StartsWith("SELL_MENU_"))
            {
                var symbol = data.Split('_')[2];
                if (Market.TryGetValue(symbol, out var c))
                {
                    var user = Users[userId];
                    var hasStock = user.Portfolio.TryGetValue(symbol, out var sq) ? sq : 0;
                    var price = Math.Max(0.01m, GetCurrentPrice(symbol) * 0.95m); // قیمت نقدشوندگی فوری (۵٪ زیر قیمت بازار)

                    string msg = $"💰 فروش سریع سهام {symbol} — قیمت نقدشوندگی واحد: {FmtPrice(price)}\n\n" +
                                 $"📦 موجودی سهام شما: {hasStock:N0} واحد\n" +
                                 $"لطفاً مقدار مورد نظر برای فروش فوری را انتخاب کنید:";

                    var kb = new InlineKeyboardMarkup(new[]
                    {
                        new[]
                        {
                            InlineKeyboardButton.WithCallbackData("💰 ۱۰ واحد", $"QUICK_SELL_{symbol}_10"),
                            InlineKeyboardButton.WithCallbackData("💰 ۵۰ واحد", $"QUICK_SELL_{symbol}_50"),
                            InlineKeyboardButton.WithCallbackData("🔥 فروش همه موجودی", $"SELL_ALL_{symbol}")
                        },
                        new[]
                        {
                            InlineKeyboardButton.WithCallbackData("🔙 بازگشت به پنل ارز", $"VIEW_SYMBOL_{symbol}")
                        }
                    });

                    await bot.SendMessage(chatId, msg, replyMarkup: kb, cancellationToken: ct);
                }
            }
            else if (data.StartsWith("QUICK_BUY_"))
            {
                var parts = data.Split('_');
                var symbol = parts[2];
                if (long.TryParse(parts[3], out var qty) && Market.TryGetValue(symbol, out var c))
                {
                    var user = Users[userId];
                    var price = GetCurrentPrice(symbol);
                    decimal totalCost = price * qty * 1.01m; // ۱٪ کارمزد

                    if (user.Balance < totalCost)
                    {
                        await bot.SendMessage(chatId, $"❌ موجودی نقدی دلار شما کافی نیست.\nمبلغ مورد نیاز با کارمزد: {FmtMoney(totalCost)}\nموجودی شما: {FmtMoney(user.Balance)}", cancellationToken: ct);
                        return;
                    }

                    var order = new Order { UserId = userId, Type = "BUY", Price = price, Quantity = qty, Timestamp = DateTime.UtcNow };
                    c.Orders.Add(order);
                    MatchOrders(symbol, userId);
                    SaveData();

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
                if (long.TryParse(parts[3], out var qty) && Market.TryGetValue(symbol, out var c))
                {
                    var user = Users[userId];
                    var hasStock = user.Portfolio.TryGetValue(symbol, out var sq) ? sq : 0;
                    if (hasStock < qty)
                    {
                        await bot.SendMessage(chatId, $"❌ موجودی سهام {symbol} شما کافی نیست. موجودی شما: {hasStock:N0} واحد", cancellationToken: ct);
                        return;
                    }

                    var price = Math.Max(0.01m, GetCurrentPrice(symbol) * 0.95m);
                    var order = new Order { UserId = userId, Type = "SELL", Price = price, Quantity = qty, Timestamp = DateTime.UtcNow };
                    c.Orders.Add(order);
                    MatchOrders(symbol, userId);
                    SaveData();

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
                if (Market.TryGetValue(symbol, out var c))
                {
                    var user = Users[userId];
                    var hasStock = user.Portfolio.TryGetValue(symbol, out var sq) ? sq : 0;
                    if (hasStock <= 0)
                    {
                        await bot.SendMessage(chatId, $"❌ شما هیچ موجودی از ارز {symbol} برای فروش ندارید.", cancellationToken: ct);
                        return;
                    }

                    var price = Math.Max(0.01m, GetCurrentPrice(symbol) * 0.95m);
                    var order = new Order { UserId = userId, Type = "SELL", Price = price, Quantity = hasStock, Timestamp = DateTime.UtcNow };
                    c.Orders.Add(order);
                    MatchOrders(symbol, userId);
                    SaveData();

                    await bot.SendMessage(
                        chatId,
                        $"✅ تمام موجودی {symbol} شما ({hasStock:N0} واحد) به قیمت واحد {FmtPrice(price)} فروخته شد!\n💰 موجودی جدید دلار شما: {FmtMoney(user.Balance)}",
                        replyMarkup: userId == OwnerId ? GetOwnerKeyboard() : GetUserKeyboard(userId),
                        cancellationToken: ct
                    );
                }
            }
            else if (data == "SHOW_REFERRAL")
            {
                var user = Users[userId];
                string refMsg = $"🎁 کد دعوت اختصاصی شما: {user.ReferralCode}\n\n" +
                                $"🔗 لینک دعوت مستقیم:\n" +
                                $"https://t.me/{(await bot.GetMe()).Username}?start={user.ReferralCode}\n\n" +
                                $"با دعوت هر دوست، شما مبلغ {FmtMoney(500m)} و دوست شما مبلغ {FmtMoney(200m)} پاداش اولیه دریافت می‌کند!\n" +
                                $"👥 دوستان دعوت‌شده: {user.Referrals} نفر";
                await bot.SendMessage(chatId, refMsg, cancellationToken: ct);
            }
        }

        // ===================== HELPER METHODS & UI CARDS =====================
        private static async Task SendSymbolCardAsync(ITelegramBotClient bot, long chatId, string symbol, CancellationToken ct)
        {
            if (!Market.TryGetValue(symbol, out var c))
            {
                await bot.SendMessage(chatId, $"❌ ارز {symbol} یافت نشد.", cancellationToken: ct);
                return;
            }

            decimal currentPrice = GetCurrentPrice(symbol);
            decimal baseVal = c.BaseValue;
            decimal changePct = baseVal > 0 ? ((currentPrice - baseVal) / baseVal) * 100m : 0m;
            int openSells = c.Orders.Count(o => o.Type == "SELL");
            int openBuys = c.Orders.Count(o => o.Type == "BUY");

            string caption = $"🏷 ارز: {c.Symbol} — {c.Description}\n\n" +
                             $"💵 قیمت لحظه‌ای: {FmtPrice(currentPrice)} ({(changePct >= 0 ? "+" : "")}{changePct:N1}% نسبت به قیمت پایه)\n" +
                             $"💎 قیمت پایه: {FmtPrice(baseVal)}\n" +
                             $"📦 عرضه در گردش: {c.CirculatingSupply:N0} از {c.TotalSupply:N0} واحد\n" +
                             $"📋 سفارشات فعال بازار: {openSells} فروش | {openBuys} خرید\n\n" +
                             $"💡 برای خرید، فروش فوری یا مشاهده نمودار، روی دکمه‌های زیر کلیک کنید:";

            var inlineKb = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("🛒 خرید فوری", $"BUY_MENU_{symbol}"),
                    InlineKeyboardButton.WithCallbackData("💰 فروش فوری", $"SELL_MENU_{symbol}")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("📈 چارت گرافیکی", $"CHART_{symbol}"),
                    InlineKeyboardButton.WithCallbackData("🔄 به‌روزرسانی", $"VIEW_SYMBOL_{symbol}")
                }
            });

            bool photoSent = false;
            if (!string.IsNullOrWhiteSpace(c.PhotoUrl))
            {
                try
                {
                    await bot.SendPhoto(
                        chatId,
                        InputFile.FromString(c.PhotoUrl),
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
            foreach (var c in Market.Values)
            {
                decimal price = GetCurrentPrice(c.Symbol);
                decimal changePct = c.BaseValue > 0 ? ((price - c.BaseValue) / c.BaseValue) * 100m : 0m;
                string arrow = changePct >= 0 ? "🟢" : "🔴";
                msg += $"{arrow} {c.Symbol} | قیمت: {FmtPrice(price)} ({(changePct >= 0 ? "+" : "")}{changePct:N1}%)\n";
            }
            msg += "\n💡 برای مشاهده کارت اختصاصی هر ارز، تصویر و معامله فوری، روی نماد مورد نظر کلیک کنید:";

            var symbolButtons = Market.Keys.Select(sym =>
                InlineKeyboardButton.WithCallbackData($"📊 {sym} ({FmtPrice(GetCurrentPrice(sym))})", $"VIEW_SYMBOL_{sym}")
            ).ToList();

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

            var symbolButtons = Market.Keys.Select(sym =>
                InlineKeyboardButton.WithCallbackData($"🛒 خرید {sym} ({FmtPrice(GetCurrentPrice(sym))})", $"BUY_MENU_{sym}")
            ).ToList();

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
            string msg = "💰 منوی انتخاب ارز برای فروش سهام:\n\n" +
                         "ارز مورد نظر را از لیست زیر انتخاب کنید یا از دستور متنی زیر استفاده کنید:\n" +
                         "مثال: فروش BTC 5 65000";

            var symbolButtons = Market.Keys.Select(sym =>
                InlineKeyboardButton.WithCallbackData($"💰 فروش {sym}", $"SELL_MENU_{sym}")
            ).ToList();

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

            var symbolButtons = Market.Keys.Select(sym =>
                InlineKeyboardButton.WithCallbackData($"📈 چارت {sym}", $"CHART_{sym}")
            ).ToList();

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

        private static async Task SendGraphicChartAsync(ITelegramBotClient bot, long chatId, string symbol, CancellationToken ct)
        {
            if (!Market.TryGetValue(symbol, out var c))
            {
                await bot.SendMessage(chatId, $"❌ ارز {symbol} یافت نشد.", cancellationToken: ct);
                return;
            }

            var prices = c.PriceHistory.TakeLast(30).ToArray();
            if (prices.Length == 0)
            {
                prices = new[] { c.BaseValue };
            }

            try
            {
                var plt = new Plot();
                var scatter = plt.Add.Scatter(
                    Enumerable.Range(1, prices.Length).Select(i => (double)i).ToArray(),
                    prices.Select(p => (double)p).ToArray()
                );
                scatter.LineWidth = 3;
                scatter.MarkerSize = 7;
                plt.Title($"{symbol} Price History ($)");
                plt.XLabel("Trades");
                plt.YLabel("Price ($)");
                plt.Axes.Left.Label.Text = "Price ($)";

                string filePath = $"{symbol}_chart_{DateTime.UtcNow.Ticks}.png";
                plt.SavePng(filePath, 800, 400);

                await using var stream = IOFile.OpenRead(filePath);
                await bot.SendPhoto(
                    chatId,
                    InputFile.FromStream(stream, filePath),
                    caption: $"📈 چارت گرافیکی روند قیمت {symbol}\n💵 قیمت فعلی: {FmtPrice(GetCurrentPrice(symbol))}\n💎 قیمت پایه: {FmtPrice(c.BaseValue)}",
                    cancellationToken: ct
                );
                IOFile.Delete(filePath);
            }
            catch
            {
                await bot.SendMessage(chatId, $"📉 تاریخچه قیمت‌های {symbol} ($):\n" + string.Join(" → ", prices.Select(p => FmtPrice(p))), cancellationToken: ct);
            }
        }

        private static async Task SendLeaderboardAsync(ITelegramBotClient bot, long chatId, long currentUserId, CancellationToken ct)
        {
            var leaderboard = Users.Values
                .Where(u => u.UserId != 0)
                .OrderByDescending(u => u.Balance + u.Portfolio.Sum(p => p.Value * GetCurrentPrice(p.Key)))
                .Take(10)
                .ToList();

            string lb = "🏆 لیدربورد ۱۰ معامله‌گر برتر بازار (ارزش کل حساب):\n\n";
            for (int i = 0; i < leaderboard.Count; i++)
            {
                var u = leaderboard[i];
                decimal net = u.Balance + u.Portfolio.Sum(p => p.Value * GetCurrentPrice(p.Key));
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
                new KeyboardButton[] { "📊 بازار و قیمت‌ها", "💼 پورتفولیو من", "💰 موجودی من" },
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
                new KeyboardButton[] { "🏦 تزریق نقدینگی", "🖼 تنظیم عکس ارز", "📝 تنظیم توضیحات ارز" },
                new KeyboardButton[] { "🗑 حذف ارز", "📈 تنظیم قیمت دستی", "📋 سفارشات باز" },
                new KeyboardButton[] { "🔄 ریست بازار", "🎲 رویداد تصادفی", "📰 رویدادهای ویژه" },
                new KeyboardButton[] { "🏆 لیدربورد", "🔙 بازگشت به منوی اصلی" }
            }) { ResizeKeyboard = true };
        }

        private static string GetCompleteHelpText()
        {
            return @"📖 راهنمای کامل بات شبیه‌ساز بازار سهام (StockBot v2)

🌟 به دنیای جذاب شبیه‌ساز معاملات سهام خوش آمدید! تمام حساب‌ها، موجودی‌ها و معاملات بر حسب دلار ($) محاسبه می‌شوند.

---
🔘 دکمه‌های منوی پایین صفحه:
• 📊 بازار و قیمت‌ها: مشاهده لیست قیمت‌های لحظه‌ای بازار و ورود به صفحه اختصاصی هر ارز همراه با عکس و امکان خرید/فروش فوری
• 💼 پورتفولیو من: مشاهده سبد دارایی‌ها و ارزش کل حساب ($)
• 💰 موجودی من: مشاهده موجودی نقدی دلار ($) و سطح کاربری (Level / XP)
• 🛒 خرید سهام و 💰 فروش سهام: منوی سریع خرید و فروش هر ارز
• 📈 نمودار و چارت: دریافت نمودار گرافیکی روند قیمت ارزها (ScottPlot)
• 🏆 لیدربورد برترین‌ها: مشاهده ۱۰ معامله‌گر برتر با بیشترین ارزش دارایی
• 🎁 دعوت دوستان: دریافت لینک دعوت و پاداش $500.00 برای هر دعوت

---
⌨️ دستورات متنی سریع (بدون اسلش):
• بازار — مشاهده بازار و قیمت‌ها
• خرید BTC 10 65000 — خرید ۱۰ واحد بیت‌کوین به قیمت واحد ۶۵۰۰۰ دلار
• فروش BTC 5 65000 — فروش ۵ واحد بیت‌کوین به قیمت واحد ۶۵۰۰۰ دلار
• چارت BTC — دریافت نمودار گرافیکی بیت‌کوین
• پنل ارز BTC — مشاهده اطلاعات کامل، تصویر و سفارشات باز
• پورتفولیو — مشاهده دارایی‌ها
• موجودی — مشاهده موجودی دلار

---
💡 نکات طلایی:
۱. 🏦 نقدینگی اولیه و قیمت پایه تمام ارزها توسط خزانه مرکزی در اوردربوک تأمین شده است؛ شما در هر لحظه می‌توانید با یک کلیک خرید و فروش کنید!
۲. 🌙 پاداش شبانه: هر شب ساعت ۱۲ (به وقت تهران)، اگر موجودی نقدی شما کمتر از $5,000.00 باشد، مبلغ $1,000.00 هدیه به حسابتان واریز می‌شود!";
        }

        // ===================== MATCHING ENGINE =====================
        private static void MatchOrders(string symbol, long triggeredByUserId)
        {
            if (!Market.TryGetValue(symbol, out var currency)) return;
            var buys = currency.Orders.Where(o => o.Type == "BUY" && o.Quantity > 0).OrderByDescending(o => o.Price).ToList();
            var sells = currency.Orders.Where(o => o.Type == "SELL" && o.Quantity > 0).OrderBy(o => o.Price).ToList();

            foreach (var buy in buys)
            {
                foreach (var sell in sells)
                {
                    if (buy.Quantity <= 0 || sell.Quantity <= 0) continue;
                    if (buy.Price >= sell.Price)
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
                                buyer.Balance -= (tradePrice * matchQty) + tax;
                                if (!buyer.Portfolio.ContainsKey(symbol)) buyer.Portfolio[symbol] = 0;
                                buyer.Portfolio[symbol] += matchQty;
                            }

                            if (sell.UserId != 0)
                            {
                                seller.Balance += (tradePrice * matchQty) - tax;
                                if (!seller.Portfolio.ContainsKey(symbol)) seller.Portfolio[symbol] = 0;
                                seller.Portfolio[symbol] -= matchQty;
                                if (seller.Portfolio[symbol] < 0) seller.Portfolio[symbol] = 0;
                            }

                            buy.Quantity -= matchQty;
                            sell.Quantity -= matchQty;

                            currency.PriceHistory.Add(tradePrice);
                            if (currency.PriceHistory.Count > 100) currency.PriceHistory.RemoveAt(0);

                            if (buy.UserId != 0 && sell.UserId != 0)
                            {
                                UpdateUserStats(buyer, seller);
                            }
                        }
                    }
                }
            }
            currency.Orders.RemoveAll(o => o.Quantity <= 0);
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
                        foreach (var user in Users.Values.Where(u => u.UserId != 0))
                        {
                            if (user.Balance < 5000m && (DateTime.UtcNow - user.LastDailyReward).TotalHours > 20)
                            {
                                user.Balance += 1000m;
                                user.LastDailyReward = DateTime.UtcNow;
                                try
                                {
                                    await Bot.SendMessage(user.UserId, $"🌙 پاداش شبانه: مبلغ {FmtMoney(1000m)} هدیه به حساب شما واریز شد!\n💰 موجودی جدید: {FmtMoney(user.Balance)}");
                                }
                                catch { }
                            }
                        }
                        SaveData();
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
                    if (IOFile.Exists(DataFile))
                    {
                        await using var stream = IOFile.OpenRead(DataFile);
                        await Bot.SendDocument(
                            OwnerId,
                            InputFile.FromStream(stream, "stockbot_data.json"),
                            caption: $"📦 بک‌آپ خودکار دیتابیس - {DateTime.Now:yyyy-MM-dd HH:mm}"
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
