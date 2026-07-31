// StockBot.cs - Telegram Stock Market Bot v2 (No Slash Commands)
// تک‌فایل C# کامل - بدون هیچ دستوری که با / شروع شود
// برای چارت گرافیکی: dotnet add package ScottPlot

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

public class StockBot
{
    private static readonly string Token = "8871928516:AAGChm-ApCPvd53KZDD8pIr1CEfYISCcLqI";
    private static readonly long OwnerId = 8248899977;
    private static ITelegramBotClient Bot;

    public class Currency
    {
        public string Symbol { get; set; }
        public long TotalSupply { get; set; }
        public decimal BaseValue { get; set; }
        public long CirculatingSupply { get; set; }
        public List<Order> Orders { get; set; } = new();
        public List<decimal> PriceHistory { get; set; } = new();
    }

    public class Order
    {
        public long UserId { get; set; }
        public string Type { get; set; }
        public decimal Price { get; set; }
        public long Quantity { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class User
    {
        public long UserId { get; set; }
        public string Username { get; set; }
        public decimal Balance { get; set; } = 5000;
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
        public string ReferralCode { get; set; }
        public int Referrals { get; set; } = 0;
    }

    private static Dictionary<string, Currency> Market = new();
    private static Dictionary<long, User> Users = new();
    private static Dictionary<long, string> UserStates = new();
    private static readonly string DataFile = "stockbot_data.json";

    public static async Task Main(string[] args)
    {
        LoadData();
        Bot = new TelegramBotClient(Token);
        var me = await Bot.GetMeAsync();
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
            var json = IOFile.ReadAllText(DataFile);
            var data = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
            if (data != null)
            {
                if (data.ContainsKey("Market")) Market = JsonConvert.DeserializeObject<Dictionary<string, Currency>>(data["Market"].ToString());
                if (data.ContainsKey("Users")) Users = JsonConvert.DeserializeObject<Dictionary<long, User>>(data["Users"].ToString());
            }
        }
    }

    private static void SaveData()
    {
        var data = new { Market, Users };
        IOFile.WriteAllText(DataFile, JsonConvert.SerializeObject(data, Formatting.Indented));
    }

    private static async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
    {
        if (update.Message is not { } message || message.From is null) return;
        var chatId = message.Chat.Id;
        var userId = message.From.Id;
        var text = message.Text?.Trim() ?? "";

        if (!Users.ContainsKey(userId))
        {
            Users[userId] = new User { UserId = userId, Username = message.From.Username ?? "unknown" };
            Users[userId].DeviceFingerprints.Add(userId % 100000);
            Users[userId].ReferralCode = "REF" + userId.ToString().Substring(Math.Max(0, userId.ToString().Length - 6));
        }

        if (userId == OwnerId)
            await HandleOwnerCommands(bot, message, text, ct);
        else
            await HandleUserCommands(bot, message, text, ct);

        SaveData();
    }

    // ===================== OWNER PANEL (گسترش یافته و خفن) =====================
    private static async Task HandleOwnerCommands(ITelegramBotClient bot, Message message, string text, CancellationToken ct)
    {
        var chatId = message.Chat.Id;

        if (text == "پنل" || text == "start" || text == "راهنما")
        {
            var keyboard = new ReplyKeyboardMarkup(new[]
            {
                new KeyboardButton[] { "➕ اضافه کردن ارز", "📊 مرور بازار" },
                new KeyboardButton[] { "💰 موجودی کاربران", "🗑 حذف ارز" },
                new KeyboardButton[] { "📈 تنظیم قیمت دستی", "📋 سفارشات باز" },
                new KeyboardButton[] { "🔄 ریست بازار", "راهنما" },
                new KeyboardButton[] { "🎲 رویداد تصادفی", "🏆 لیدربورد" }
            }) { ResizeKeyboard = true };

            await bot.SendTextMessageAsync(chatId, "👑 پنل مدیریت خفن - Owner Control Panel", replyMarkup: keyboard, cancellationToken: ct);
            return;
        }

        // اضافه کردن ارز
        if (text == "➕ اضافه کردن ارز")
        {
            UserStates[chatId] = "ADD_SYMBOL";
            await bot.SendTextMessageAsync(chatId, "نماد ارز را وارد کنید (مثال: BTC):", cancellationToken: ct);
        }
        else if (UserStates.TryGetValue(chatId, out var state) && state == "ADD_SYMBOL")
        {
            UserStates[chatId] = $"ADD_SUPPLY_{text.ToUpper()}";
            await bot.SendTextMessageAsync(chatId, $"تعداد کل عرضه {text.ToUpper()} را وارد کنید:", cancellationToken: ct);
        }
        else if (UserStates.TryGetValue(chatId, out state) && state.StartsWith("ADD_SUPPLY_"))
        {
            var symbol = state.Split('_')[2];
            if (long.TryParse(text, out var supply))
            {
                UserStates[chatId] = $"ADD_BASE_{symbol}_{supply}";
                await bot.SendTextMessageAsync(chatId, "ارزش پایه هر واحد را وارد کنید:", cancellationToken: ct);
            }
        }
        else if (UserStates.TryGetValue(chatId, out state) && state.StartsWith("ADD_BASE_"))
        {
            var parts = state.Split('_');
            var symbol = parts[2];
            var supply = long.Parse(parts[3]);
            if (decimal.TryParse(text, out var baseVal))
            {
                Market[symbol] = new Currency { Symbol = symbol, TotalSupply = supply, BaseValue = baseVal, CirculatingSupply = supply / 2 };
                Market[symbol].PriceHistory.Add(baseVal);
                UserStates.Remove(chatId);
                await bot.SendTextMessageAsync(chatId, $"✅ ارز {symbol} با موفقیت اضافه شد!", cancellationToken: ct);
            }
        }

        // مرور بازار
        else if (text == "📊 مرور بازار")
        {
            string msg = "📈 بازار فعلی:\n";
            foreach (var c in Market.Values)
            {
                decimal price = c.PriceHistory.LastOrDefault(c.BaseValue);
                msg += $"{c.Symbol} | قیمت: {price} | عرضه: {c.CirculatingSupply}/{c.TotalSupply}\n";
            }
            await bot.SendTextMessageAsync(chatId, msg, cancellationToken: ct);
        }

        // حذف ارز
        else if (text == "🗑 حذف ارز")
        {
            UserStates[chatId] = "REMOVE_CURRENCY";
            await bot.SendTextMessageAsync(chatId, "نماد ارزی که می‌خواهید حذف کنید را وارد کنید:", cancellationToken: ct);
        }
        else if (UserStates.TryGetValue(chatId, out state) && state == "REMOVE_CURRENCY")
        {
            if (Market.ContainsKey(text.ToUpper()))
            {
                Market.Remove(text.ToUpper());
                UserStates.Remove(chatId);
                await bot.SendTextMessageAsync(chatId, "✅ ارز حذف شد.", cancellationToken: ct);
            }
        }

        // تنظیم قیمت دستی
        else if (text == "📈 تنظیم قیمت دستی")
        {
            UserStates[chatId] = "SET_PRICE_SYMBOL";
            await bot.SendTextMessageAsync(chatId, "نماد ارز را وارد کنید:", cancellationToken: ct);
        }
        else if (UserStates.TryGetValue(chatId, out state) && state == "SET_PRICE_SYMBOL")
        {
            UserStates[chatId] = $"SET_PRICE_{text.ToUpper()}";
            await bot.SendTextMessageAsync(chatId, "قیمت جدید را وارد کنید:", cancellationToken: ct);
        }
        else if (UserStates.TryGetValue(chatId, out state) && state.StartsWith("SET_PRICE_"))
        {
            var symbol = state.Split('_')[2];
            if (decimal.TryParse(text, out var newPrice) && Market.ContainsKey(symbol))
            {
                Market[symbol].PriceHistory.Add(newPrice);
                UserStates.Remove(chatId);
                await bot.SendTextMessageAsync(chatId, $"✅ قیمت {symbol} به {newPrice} تغییر کرد.", cancellationToken: ct);
            }
        }

        // سفارشات باز
        else if (text == "📋 سفارشات باز")
        {
            string msg = "📋 سفارشات باز بازار:\n";
            foreach (var c in Market.Values)
            {
                foreach (var o in c.Orders.Take(5))
                    msg += $"{c.Symbol} | {o.Type} | {o.Quantity} @ {o.Price}\n";
            }
            await bot.SendTextMessageAsync(chatId, msg, cancellationToken: ct);
        }

        // ریست بازار
        else if (text == "🔄 ریست بازار")
        {
            Market.Clear();
            await bot.SendTextMessageAsync(chatId, "⚠️ بازار کاملاً ریست شد!", cancellationToken: ct);
        }

        // لیدربورد
        else if (text == "🏆 لیدربورد")
        {
            var leaderboard = Users.Values
                .OrderByDescending(u => u.Balance + u.Portfolio.Sum(p => p.Value * (Market.ContainsKey(p.Key) ? Market[p.Key].PriceHistory.LastOrDefault(1) : 1)))
                .Take(10)
                .Select((u, i) => $"{i + 1}. @{u.Username} - ارزش کل: {u.Balance + u.Portfolio.Sum(p => p.Value * (Market.ContainsKey(p.Key) ? Market[p.Key].PriceHistory.LastOrDefault(1) : 1))}")
                .ToList();

            string lb = "🏆 لیدربورد برترین‌ها (ارزش کل دارایی):\n" + string.Join("\n", leaderboard);
            await bot.SendTextMessageAsync(chatId, lb, cancellationToken: ct);
        }

        // رویداد تصادفی (در پنل ادمین)
        else if (text == "🎲 رویداد تصادفی")
        {
            if (Market.Count > 0)
            {
                var random = new Random();
                var symbol = Market.Keys.ElementAt(random.Next(Market.Count));
                var currency = Market[symbol];
                var change = random.Next(-30, 31);
                var newPrice = Math.Max(1, currency.PriceHistory.LastOrDefault(currency.BaseValue) * (1 + change / 100m));

                currency.PriceHistory.Add(newPrice);

                string eventMsg = $"🎲 رویداد تصادفی!\nارز {symbol} {change:+0;-0}% تغییر کرد!\nقیمت جدید: {newPrice}";
                await bot.SendTextMessageAsync(chatId, eventMsg, cancellationToken: ct);

                // اطلاع‌رسانی به همه کاربران
                foreach (var uid in Users.Keys)
                {
                    try { await bot.SendTextMessageAsync(uid, eventMsg); } catch { }
                }
            }
        }

        // رویدادهای قابل انتخاب توسط ادمین
        else if (text == "رویدادها")
        {
            var keyboard = new ReplyKeyboardMarkup(new[]
            {
                new KeyboardButton[] { "خبر مثبت", "خبر منفی", "هک", "جنگ", "رکود" },
                new KeyboardButton[] { "رشد ناگهانی", "سقوط آزاد", "بازگشت" }
            }) { ResizeKeyboard = true };
            await bot.SendTextMessageAsync(chatId, "انتخاب رویداد:", replyMarkup: keyboard, cancellationToken: ct);
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
                    "خبر مثبت" => 25,
                    "خبر منفی" => -20,
                    "هک" => -40,
                    "جنگ" => -35,
                    "رکود" => -15,
                    "رشد ناگهانی" => 50,
                    "سقوط آزاد" => -60,
                    "بازگشت" => 30,
                    _ => 0
                };

                var newPrice = Math.Max(1, currency.PriceHistory.LastOrDefault(currency.BaseValue) * (1 + change / 100m));
                currency.PriceHistory.Add(newPrice);

                string eventMsg = $"📰 رویداد: {text}\nارز {symbol} {change:+0;-0}% تغییر کرد!\nقیمت جدید: {newPrice}";
                await bot.SendTextMessageAsync(chatId, eventMsg, cancellationToken: ct);

                foreach (var uid in Users.Keys)
                {
                    try { await bot.SendTextMessageAsync(uid, eventMsg); } catch { }
                }
            }
        }
    }

    // ===================== USER COMMANDS (بدون /) =====================
    private static async Task HandleUserCommands(ITelegramBotClient bot, Message message, string text, CancellationToken ct)
    {
        var chatId = message.Chat.Id;
        var userId = message.From.Id;
        var user = Users[userId];

        // راهنما
        if (text == "راهنما" || text == "help")
        {
            string help = @"📖 راهنمای بات سهام

بازار → نمایش تمام ارزها
نمودار BTC → نمایش چارت ارز
خرید BTC 10 5000 → خرید ۱۰ واحد BTC به قیمت ۵۰۰۰
فروش BTC 5 4800 → فروش ۵ واحد
پورتفولیو → دارایی شما
موجودی → موجودی فعلی
پنل ارز BTC → پنل کامل ارز (سفارشات + تاریخچه)

هر شب ساعت ۱۲ شب تهران اگر موجودی کمتر از ۵۰۰۰ باشد ۱۰۰۰ واحد هدیه می‌گیری!";
            await bot.SendTextMessageAsync(chatId, help, cancellationToken: ct);
            return;
        }

        if (text == "بازار")
        {
            string msg = "📊 بازار ارزها:\n";
            foreach (var c in Market)
                msg += $"{c.Key} - قیمت فعلی: {c.Value.PriceHistory.LastOrDefault(c.Value.BaseValue)}\n";
            await bot.SendTextMessageAsync(chatId, msg, cancellationToken: ct);
        }
        else if (text.StartsWith("نمودار "))
        {
            var symbol = text.Split(' ')[1].ToUpper();
            if (Market.ContainsKey(symbol))
            {
                var c = Market[symbol];
                string chart = $"📉 چارت {symbol} (۱۰ قیمت آخر):\n";
                foreach (var p in c.PriceHistory.TakeLast(10))
                    chart += $"{p} ";
                await bot.SendTextMessageAsync(chatId, chart, cancellationToken: ct);
            }
        }
        else if (text.StartsWith("چارت "))
        {
            var symbol = text.Split(' ')[1].ToUpper();
            if (Market.ContainsKey(symbol))
            {
                var c = Market[symbol];
                var prices = c.PriceHistory.TakeLast(30).ToArray();
                if (prices.Length > 1)
                {
                    var plt = new Plot();
                    plt.Add.Scatter(Enumerable.Range(0, prices.Length).Select(i => (double)i).ToArray(), prices.Select(p => (double)p).ToArray());
                    plt.Title($"{symbol} Price Chart");
                    plt.XLabel("Time");
                    plt.YLabel("Price");

                    string filePath = $"{symbol}_chart.png";
                    plt.SavePng(filePath, 800, 400);

                    await using var stream = IOFile.OpenRead(filePath);
                    await bot.SendPhotoAsync(chatId, InputFile.FromStream(stream, filePath), caption: $"📊 چارت گرافیکی {symbol}", cancellationToken: ct);
                    IOFile.Delete(filePath);
                }
                else
                {
                    await bot.SendTextMessageAsync(chatId, "داده کافی برای چارت وجود ندارد.", cancellationToken: ct);
                }
            }
        }
        else if (text.StartsWith("پنل ارز "))
        {
            var symbol = text.Split(' ')[2].ToUpper();
            if (Market.ContainsKey(symbol))
            {
                var c = Market[symbol];
                string panel = $"📋 پنل {symbol}\n";
                panel += $"قیمت فعلی: {c.PriceHistory.LastOrDefault(c.BaseValue)}\n";
                panel += $"سفارشات باز: {c.Orders.Count}\n";
                panel += $"تاریخچه قیمت: {string.Join(" | ", c.PriceHistory.TakeLast(5))}\n";
                await bot.SendTextMessageAsync(chatId, panel, cancellationToken: ct);
            }
        }
        else if (text.StartsWith("خرید "))
        {
            var parts = text.Split(' ');
            if (parts.Length == 4 && long.TryParse(parts[2], out var qty) && decimal.TryParse(parts[3], out var price))
            {
                var symbol = parts[1].ToUpper();
                if (Market.ContainsKey(symbol))
                {
                    var order = new Order { UserId = userId, Type = "BUY", Price = price, Quantity = qty, Timestamp = DateTime.UtcNow };
                    Market[symbol].Orders.Add(order);
                    MatchOrders(symbol, userId);
                    await bot.SendTextMessageAsync(chatId, $"✅ سفارش خرید ثبت شد", cancellationToken: ct);
                }
            }
        }
        else if (text.StartsWith("فروش "))
        {
            var parts = text.Split(' ');
            if (parts.Length == 4 && long.TryParse(parts[2], out var qty) && decimal.TryParse(parts[3], out var price))
            {
                var symbol = parts[1].ToUpper();
                if (Market.ContainsKey(symbol))
                {
                    var order = new Order { UserId = userId, Type = "SELL", Price = price, Quantity = qty, Timestamp = DateTime.UtcNow };
                    Market[symbol].Orders.Add(order);
                    MatchOrders(symbol, userId);
                    await bot.SendTextMessageAsync(chatId, $"✅ سفارش فروش ثبت شد", cancellationToken: ct);
                }
            }
        }
        else if (text == "پورتفولیو")
        {
            string p = $"💼 پورتفولیو شما - موجودی: {user.Balance}\n";
            foreach (var h in user.Portfolio) p += $"{h.Key}: {h.Value}\n";
            await bot.SendTextMessageAsync(chatId, p, cancellationToken: ct);
        }
        else if (text == "موجودی")
        {
            await bot.SendTextMessageAsync(chatId, $"💰 موجودی فعلی شما: {user.Balance}", cancellationToken: ct);
        }
        else if (text == "دعوت")
        {
            string refMsg = $"🎁 کد دعوت شما: {user.ReferralCode}\n" +
                           $"هر نفر که با کد شما جوین بشه ۵۰۰ سکه هدیه می‌گیرید!\n" +
                           $"تعداد دعوت‌شده‌ها: {user.Referrals}";
            await bot.SendTextMessageAsync(chatId, refMsg, cancellationToken: ct);
        }
        else if (text.StartsWith("دعوت "))
        {
            var code = text.Split(' ')[1];
            var referrer = Users.Values.FirstOrDefault(u => u.ReferralCode == code);
            if (referrer != null && referrer.UserId != userId && user.Referrals == 0)
            {
                referrer.Balance += 500;
                referrer.Referrals++;
                user.Balance += 200; // new user reward
                await bot.SendTextMessageAsync(chatId, "✅ با موفقیت دعوت شدید! ۲۰۰ سکه هدیه گرفتید.", cancellationToken: ct);
                try
                {
                    await bot.SendTextMessageAsync(referrer.UserId, $"🎉 یکی با کد شما جوین شد! +۵۰۰ سکه");
                }
                catch { }
            }
            else
            {
                await bot.SendTextMessageAsync(chatId, "کد نامعتبر یا قبلاً استفاده شده.", cancellationToken: ct);
            }
        }
    }

    private static void MatchOrders(string symbol, long currentUser)
    {
        var currency = Market[symbol];
        var buys = currency.Orders.Where(o => o.Type == "BUY").OrderByDescending(o => o.Price).ToList();
        var sells = currency.Orders.Where(o => o.Type == "SELL").OrderBy(o => o.Price).ToList();

        foreach (var buy in buys)
        {
            foreach (var sell in sells)
            {
                if (buy.Price >= sell.Price && buy.Quantity > 0 && sell.Quantity > 0)
                {
                    var matchQty = Math.Min(buy.Quantity, sell.Quantity);
                    var tradePrice = sell.Price;

                    if (Users.ContainsKey(buy.UserId) && Users.ContainsKey(sell.UserId))
                    {
                        var buyer = Users[buy.UserId];
                        var seller = Users[sell.UserId];

                    if (buyer.Balance >= tradePrice * matchQty)
                    {
                        decimal tax = tradePrice * matchQty * 0.01m; // ۱٪ مالیات

                        buyer.Balance -= tradePrice * matchQty + tax;
                        seller.Balance += tradePrice * matchQty - tax;

                        if (!buyer.Portfolio.ContainsKey(symbol)) buyer.Portfolio[symbol] = 0;
                        buyer.Portfolio[symbol] += matchQty;

                        if (!seller.Portfolio.ContainsKey(symbol)) seller.Portfolio[symbol] = 0;
                        seller.Portfolio[symbol] -= matchQty;

                        buy.Quantity -= matchQty;
                        sell.Quantity -= matchQty;

                        currency.PriceHistory.Add(tradePrice);
                        if (currency.PriceHistory.Count > 50) currency.PriceHistory.RemoveAt(0);

                        // XP System
                        UpdateUserStats(buyer, seller, tradePrice, matchQty);
                    }
                    }
                }
            }
        }
        currency.Orders.RemoveAll(o => o.Quantity <= 0);
    }

    private static void UpdateUserStats(User buyer, User seller, decimal price, long qty)
    {
        // Buyer stats
        buyer.TotalTrades++;
        buyer.XP += 10;
        if (buyer.XP >= buyer.Level * 100)
        {
            buyer.Level++;
            buyer.XP = 0;
        }

        // Seller stats
        seller.TotalTrades++;
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
            var tehran = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Iran Standard Time"));
            if (tehran.Hour == 0 && tehran.Minute == 0)
            {
                foreach (var user in Users.Values)
                {
                    if (user.Balance < 5000 && (DateTime.UtcNow - user.LastDailyReward).TotalHours > 20)
                    {
                        user.Balance += 1000;
                        user.LastDailyReward = DateTime.UtcNow;
                        try { await Bot.SendTextMessageAsync(user.UserId, "🌙 هدیه شبانه: +۱۰۰۰ واحد!"); } catch { }
                    }
                }
                SaveData();
            }
            await Task.Delay(60000);
        }
    }

    private static async Task DatabaseBackupScheduler()
    {
        while (true)
        {
            try
            {
                if (IOFile.Exists(DataFile))
                {
                    await using var stream = IOFile.OpenRead(DataFile);
                    await Bot.SendDocumentAsync(
                        OwnerId,
                        InputFile.FromStream(stream, "stockbot_data.json"),
                        caption: $"📦 بک‌آپ خودکار دیتابیس - {DateTime.Now:yyyy-MM-dd HH:mm}"
                    );
                }
            }
            catch { }

            await Task.Delay(TimeSpan.FromHours(6)); // هر ۶ ساعت
        }
    }

    private static Task HandleErrorAsync(ITelegramBotClient bot, Exception exception, CancellationToken ct)
    {
        Console.WriteLine($"Error: {exception.Message}");
        return Task.CompletedTask;
    }
}