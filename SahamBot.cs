// ============================================================================
// SahamBot - Telegram Stock Market Simulation Game
// Single-file C# Telegram Bot
// ============================================================================

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace SahamBot
{
    // ========================================================================
    // Data Models
    // ========================================================================

    public class User
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("username")]
        public string Username { get; set; } = "";

        [JsonPropertyName("first_name")]
        public string FirstName { get; set; } = "";

        [JsonPropertyName("balance")]
        public decimal Balance { get; set; } = 5000;

        [JsonPropertyName("portfolio")]
        public Dictionary<string, decimal> Portfolio { get; set; } = new();

        [JsonPropertyName("is_banned")]
        public bool IsBanned { get; set; } = false;

        [JsonPropertyName("is_suspended")]
        public bool IsSuspended { get; set; } = false;

        [JsonPropertyName("registered_at")]
        public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;

        [JsonPropertyName("last_active")]
        public DateTime LastActive { get; set; } = DateTime.UtcNow;

        [JsonPropertyName("last_daily")]
        public DateTime? LastDaily { get; set; } = null;

        [JsonPropertyName("fingerprint")]
        public string Fingerprint { get; set; } = "";

        [JsonPropertyName("alt_accounts")]
        public List<long> AltAccounts { get; set; } = new();

        [JsonPropertyName("flagged")]
        public bool Flagged { get; set; } = false;

        [JsonPropertyName("trade_count")]
        public int TradeCount { get; set; } = 0;

        [JsonPropertyName("total_volume")]
        public decimal TotalVolume { get; set; } = 0;
    }

    public class Coin
    {
        [JsonPropertyName("symbol")]
        public string Symbol { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("total_supply")]
        public decimal TotalSupply { get; set; }

        [JsonPropertyName("base_value")]
        public decimal BaseValue { get; set; }

        [JsonPropertyName("current_price")]
        public decimal CurrentPrice { get; set; }

        [JsonPropertyName("circulating_supply")]
        public decimal CirculatingSupply { get; set; }

        [JsonPropertyName("market_cap")]
        public decimal MarketCap { get; set; }

        [JsonPropertyName("is_active")]
        public bool IsActive { get; set; } = true;

        [JsonPropertyName("is_suspended")]
        public bool IsSuspended { get; set; } = false;

        [JsonPropertyName("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [JsonPropertyName("price_history")]
        public List<PricePoint> PriceHistory { get; set; } = new();

        [JsonPropertyName("volume_24h")]
        public decimal Volume24h { get; set; } = 0;

        [JsonPropertyName("high_24h")]
        public decimal High24h { get; set; } = 0;

        [JsonPropertyName("low_24h")]
        public decimal Low24h { get; set; } = 0;

        [JsonPropertyName("change_24h")]
        public decimal Change24h { get; set; } = 0;

        [JsonPropertyName("holder_count")]
        public int HolderCount { get; set; } = 0;
    }

    public class PricePoint
    {
        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; }

        [JsonPropertyName("price")]
        public decimal Price { get; set; }

        [JsonPropertyName("volume")]
        public decimal Volume { get; set; } = 0;
    }

    public class Order
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8].ToUpper();

        [JsonPropertyName("user_id")]
        public long UserId { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; } = ""; // buy or sell

        [JsonPropertyName("symbol")]
        public string Symbol { get; set; } = "";

        [JsonPropertyName("quantity")]
        public decimal Quantity { get; set; }

        [JsonPropertyName("price")]
        public decimal Price { get; set; }

        [JsonPropertyName("filled_quantity")]
        public decimal FilledQuantity { get; set; } = 0;

        [JsonPropertyName("status")]
        public string Status { get; set; } = "open"; // open, partial, filled, cancelled

        [JsonPropertyName("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [JsonPropertyName("updated_at")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class Trade
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10].ToUpper();

        [JsonPropertyName("buy_order_id")]
        public string BuyOrderId { get; set; } = "";

        [JsonPropertyName("sell_order_id")]
        public string SellOrderId { get; set; } = "";

        [JsonPropertyName("buyer_id")]
        public long BuyerId { get; set; }

        [JsonPropertyName("seller_id")]
        public long SellerId { get; set; }

        [JsonPropertyName("symbol")]
        public string Symbol { get; set; } = "";

        [JsonPropertyName("quantity")]
        public decimal Quantity { get; set; }

        [JsonPropertyName("price")]
        public decimal Price { get; set; }

        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    public class BotDatabase
    {
        [JsonPropertyName("users")]
        public Dictionary<long, User> Users { get; set; } = new();

        [JsonPropertyName("coins")]
        public Dictionary<string, Coin> Coins { get; set; } = new();

        [JsonPropertyName("orders")]
        public List<Order> Orders { get; set; } = new();

        [JsonPropertyName("trades")]
        public List<Trade> Trades { get; set; } = new();

        [JsonPropertyName("banned_users")]
        public List<long> BannedUsers { get; set; } = new();

        [JsonPropertyName("fingerprints")]
        public Dictionary<string, List<long>> Fingerprints { get; set; } = new();

        [JsonPropertyName("broadcast_history")]
        public List<BroadcastRecord> BroadcastHistory { get; set; } = new();
    }

    public class BroadcastRecord
    {
        [JsonPropertyName("message")]
        public string Message { get; set; } = "";

        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        [JsonPropertyName("user_count")]
        public int UserCount { get; set; }
    }

    // ========================================================================
    // Telegram API Helper
    // ========================================================================

    public class TelegramApi
    {
        private readonly HttpClient _http;
        private readonly string _baseUrl;

        public TelegramApi(string token)
        {
            _http = new HttpClient();
            _http.Timeout = TimeSpan.FromSeconds(60);
            _baseUrl = $"https://api.telegram.org/bot{token}";
        }

        public async Task<JsonElement?> SendMessage(long chatId, string text, string parseMode = "HTML", object replyMarkup = null)
        {
            var payload = new Dictionary<string, object>
            {
                ["chat_id"] = chatId,
                ["text"] = text,
                ["parse_mode"] = parseMode,
                ["disable_web_page_preview"] = true
            };
            if (replyMarkup != null) payload["reply_markup"] = replyMarkup;
            return await Post("sendMessage", payload);
        }

        public async Task<JsonElement?> EditMessage(long chatId, string messageId, string text, object replyMarkup = null)
        {
            var payload = new Dictionary<string, object>
            {
                ["chat_id"] = chatId,
                ["message_id"] = messageId,
                ["text"] = text,
                ["parse_mode"] = "HTML",
                ["disable_web_page_preview"] = true
            };
            if (replyMarkup != null) payload["reply_markup"] = replyMarkup;
            return await Post("editMessageText", payload);
        }

        public async Task<JsonElement?> SendPhoto(long chatId, string photoUrl, string caption = "", string parseMode = "HTML")
        {
            var payload = new Dictionary<string, object>
            {
                ["chat_id"] = chatId,
                ["photo"] = photoUrl,
                ["caption"] = caption,
                ["parse_mode"] = parseMode
            };
            return await Post("sendPhoto", payload);
        }

        public async Task<JsonElement?> SendDocument(long chatId, string documentUrl, string caption = "")
        {
            var payload = new Dictionary<string, object>
            {
                ["chat_id"] = chatId,
                ["document"] = documentUrl,
                ["caption"] = caption
            };
            return await Post("sendDocument", payload);
        }

        public async Task<JsonElement?> AnswerCallback(string callbackId, string text = "", bool showAlert = false)
        {
            var payload = new Dictionary<string, object>
            {
                ["callback_query_id"] = callbackId
            };
            if (!string.IsNullOrEmpty(text))
            {
                payload["text"] = text;
                payload["show_alert"] = showAlert;
            }
            return await Post("answerCallbackQuery", payload);
        }

        public async Task<JsonElement?> Post(string method, Dictionary<string, object> payload)
        {
            try
            {
                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = false });
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _http.PostAsync($"{_baseUrl}/{method}", content);
                var responseBody = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[API Error] {method}: {response.StatusCode} - {responseBody}");
                    return null;
                }
                var doc = JsonDocument.Parse(responseBody);
                return doc.RootElement;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[API Exception] {method}: {ex.Message}");
                return null;
            }
        }

        public async Task<JsonElement?> GetUpdates(int offset, int timeout = 30)
        {
            try
            {
                var url = $"{_baseUrl}/getUpdates?offset={offset}&timeout={timeout}";
                var response = await _http.GetAsync(url);
                var responseBody = await response.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(responseBody);
                return doc.RootElement;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GetUpdates Error] {ex.Message}");
                return null;
            }
        }

        public async Task<JsonElement?> GetMe()
        {
            try
            {
                var response = await _http.GetAsync($"{_baseUrl}/getMe");
                var responseBody = await response.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(responseBody);
                return doc.RootElement;
            }
            catch
            {
                return null;
            }
        }
    }

    // ========================================================================
    // Inline Keyboard Builder
    // ========================================================================

    public class InlineKeyboard
    {
        private readonly List<List<Dictionary<string, object>>> _rows = new();
        private List<Dictionary<string, object>> _currentRow;

        public InlineKeyboard()
        {
            _currentRow = new List<Dictionary<string, object>>();
        }

        public InlineKeyboard AddButton(string text, string callbackData)
        {
            _currentRow.Add(new Dictionary<string, object>
            {
                ["text"] = text,
                ["callback_data"] = callbackData
            });
            return this;
        }

        public InlineKeyboard AddUrlButton(string text, string url)
        {
            _currentRow.Add(new Dictionary<string, object>
            {
                ["text"] = text,
                ["url"] = url
            });
            return this;
        }

        public InlineKeyboard NewRow()
        {
            if (_currentRow.Count > 0)
            {
                _rows.Add(_currentRow);
                _currentRow = new List<Dictionary<string, object>>();
            }
            return this;
        }

        public object Build()
        {
            if (_currentRow.Count > 0) _rows.Add(_currentRow);
            return new { inline_keyboard = _rows };
        }
    }

    // ========================================================================
    // User Session State Machine
    // ========================================================================

    public class UserSession
    {
        public string State { get; set; } = "idle";
        public Dictionary<string, string> Data { get; set; } = new();
        public DateTime LastActivity { get; set; } = DateTime.UtcNow;
    }

    // ========================================================================
    // Chart Generator
    // ========================================================================

    public class ChartGenerator
    {
        public static string GenerateAsciiChart(Coin coin, int width = 28, int height = 12)
        {
            var history = coin.PriceHistory;
            if (history == null || history.Count < 2)
            {
                return "📊 نمودار: داده کافی موجود نیست";
            }

            var points = history.TakeLast(width).ToList();
            var prices = points.Select(p => p.Price).ToList();
            var min = prices.Min();
            var max = prices.Max();
            var range = max - min;

            if (range == 0) range = 1;

            var sb = new StringBuilder();
            sb.AppendLine($"📊 <b>{coin.Symbol}</b> - نمودار قیمت");
            sb.AppendLine($"<pre>");

            var canvas = new char[height, width];
            for (int i = 0; i < height; i++)
                for (int j = 0; j < width; j++)
                    canvas[i, j] = ' ';

            // Draw grid lines
            for (int i = 0; i < height; i++)
            {
                if (i == 0 || i == height / 2 || i == height - 1)
                {
                    for (int j = 0; j < width; j++)
                        canvas[i, j] = '·';
                }
            }

            // Plot price line
            for (int i = 0; i < points.Count; i++)
            {
                int x = i;
                int y = height - 1 - (int)((prices[i] - min) / range * (height - 1));
                y = Math.Max(0, Math.Min(height - 1, y));

                canvas[y, x] = '█';

                // Connect points
                if (i > 0)
                {
                    int prevX = i - 1;
                    int prevY = height - 1 - (int)((prices[i - 1] - min) / range * (height - 1));
                    prevY = Math.Max(0, Math.Min(height - 1, prevY));

                    // Draw line between points
                    int steps = Math.Abs(y - prevY);
                    if (steps > 0)
                    {
                        for (int s = 1; s < steps; s++)
                        {
                            int interY = prevY + (y - prevY) * s / steps;
                            if (canvas[interY, prevX] == ' ' || canvas[interY, prevX] == '·')
                                canvas[interY, prevX] = '▓';
                        }
                    }
                }
            }

            // Render canvas
            for (int i = 0; i < height; i++)
            {
                var price = max - (range * i / (height - 1));
                if (i == 0) sb.Append($" {FormatPrice(max),10} │");
                else if (i == height / 2) sb.Append($" {FormatPrice((max + min) / 2),10} │");
                else if (i == height - 1) sb.Append($" {FormatPrice(min),10} │");
                else sb.Append($"             │");

                for (int j = 0; j < width; j++)
                    sb.Append(canvas[i, j]);
                sb.AppendLine();
            }

            // Bottom axis
            sb.Append("             └");
            for (int j = 0; j < width; j++) sb.Append('─');
            sb.AppendLine();

            // Time labels
            var firstTime = points.First().Timestamp;
            var lastTime = points.Last().Timestamp;
            sb.AppendLine($"              {firstTime:MM/dd}                    {lastTime:MM/dd}");
            sb.Append("</pre>");

            return sb.ToString();
        }

        public static string GenerateMiniChart(List<decimal> prices)
        {
            if (prices.Count < 5) return "📉";

            var min = prices.Min();
            var max = prices.Max();
            var range = max - min;
            if (range == 0) range = 1;

            var blocks = "▁▂▃▄▅▆▇█";
            var sb = new StringBuilder();
            var recent = prices.TakeLast(20).ToList();

            foreach (var p in recent)
            {
                int idx = (int)((p - min) / range * (blocks.Length - 1));
                idx = Math.Max(0, Math.Min(blocks.Length - 1, idx));
                sb.Append(blocks[idx]);
            }

            return sb.ToString();
        }

        private static string FormatPrice(decimal price)
        {
            if (price >= 10000) return $"{price / 1000:F0}K";
            if (price >= 1000) return $"{price / 1000:F1}K";
            return $"{price:F0}";
        }
    }

    // ========================================================================
    // Anti-Cheat System
    // ========================================================================

    public class AntiCheatSystem
    {
        private readonly BotDatabase _db;

        public AntiCheatSystem(BotDatabase db)
        {
            _db = db;
        }

        public void UpdateFingerprint(long userId, string fingerprint)
        {
            if (string.IsNullOrEmpty(fingerprint)) return;

            _db.Users[userId].Fingerprint = fingerprint;

            if (!_db.Fingerprints.ContainsKey(fingerprint))
                _db.Fingerprints[fingerprint] = new List<long>();

            if (!_db.Fingerprints[fingerprint].Contains(userId))
                _db.Fingerprints[fingerprint].Add(userId);

            // Detect multi-account
            if (_db.Fingerprints[fingerprint].Count > 1)
            {
                var alts = _db.Fingerprints[fingerprint];
                foreach (var altId in alts)
                {
                    if (_db.Users.ContainsKey(altId))
                    {
                        _db.Users[altId].AltAccounts = alts.Where(x => x != altId).ToList();
                        _db.Users[altId].Flagged = true;
                    }
                }
                Console.WriteLine($"[AntiCheat] Multi-account detected: {string.Join(", ", alts)} (fingerprint: {fingerprint[..8]})");
            }
        }

        public bool IsMultiAccount(long userId)
        {
            if (!_db.Users.ContainsKey(userId)) return false;
            return _db.Users[userId].Flagged && _db.Users[userId].AltAccounts.Count > 0;
        }

        public bool IsSelfTrading(long buyerId, long sellerId)
        {
            if (!_db.Users.ContainsKey(buyerId) || !_db.Users.ContainsKey(sellerId)) return false;

            var buyer = _db.Users[buyerId];
            var seller = _db.Users[sellerId];

            // Check if same fingerprint
            if (!string.IsNullOrEmpty(buyer.Fingerprint) && buyer.Fingerprint == seller.Fingerprint)
                return true;

            // Check if listed as alts
            if (buyer.AltAccounts.Contains(sellerId) || seller.AltAccounts.Contains(buyerId))
                return true;

            return false;
        }

        public bool CheckSuspiciousActivity(long userId, Order order)
        {
            if (!_db.Users.ContainsKey(userId)) return false;

            var user = _db.Users[userId];

            // Flag 1: New account with large trades
            if ((DateTime.UtcNow - user.RegisteredAt).TotalHours < 2 && order.Quantity * order.Price > 3000)
                return true;

            // Flag 2: Wash trading (buy and sell same coin rapidly)
            var recentOrders = _db.Orders.Where(o => o.UserId == userId && o.Symbol == order.Symbol &&
                o.CreatedAt > DateTime.UtcNow.AddMinutes(-5)).ToList();
            if (recentOrders.Count(o => o.Type != order.Type) >= 3)
                return true;

            return false;
        }

        public List<long> GetAllAltAccounts(long userId)
        {
            if (!_db.Users.ContainsKey(userId)) return new();
            var user = _db.Users[userId];
            if (!string.IsNullOrEmpty(user.Fingerprint) && _db.Fingerprints.ContainsKey(user.Fingerprint))
                return _db.Fingerprints[user.Fingerprint];
            return new();
        }
    }

    // ========================================================================
    // Order Matching Engine
    // ========================================================================

    public class MatchingEngine
    {
        private readonly BotDatabase _db;
        private readonly AntiCheatSystem _antiCheat;

        public MatchingEngine(BotDatabase db, AntiCheatSystem antiCheat)
        {
            _db = db;
            _antiCheat = antiCheat;
        }

        public List<Trade> PlaceOrder(Order order)
        {
            var trades = new List<Trade>();

            if (!_db.Coins.ContainsKey(order.Symbol)) return trades;
            var coin = _db.Coins[order.Symbol];
            if (coin.IsSuspended) return trades;

            if (!_db.Users.ContainsKey(order.UserId)) return trades;
            var user = _db.Users[order.UserId];

            if (order.Type == "buy")
            {
                // Validate buyer has enough balance
                var requiredBalance = order.Quantity * order.Price;
                if (user.Balance < requiredBalance)
                {
                    order.Status = "rejected";
                    _db.Orders.Add(order);
                    return trades;
                }

                // Reserve the balance
                user.Balance -= requiredBalance;

                // Match with existing sell orders (lowest price first)
                var matchingSells = _db.Orders
                    .Where(o => o.Type == "sell" && o.Symbol == order.Symbol &&
                                o.Status == "open" && o.Price <= order.Price)
                    .OrderBy(o => o.Price)
                    .ThenBy(o => o.CreatedAt)
                    .ToList();

                foreach (var sellOrder in matchingSells)
                {
                    if (order.Status == "filled") break;

                    // Anti-cheat: check self-trading
                    if (_antiCheat.IsSelfTrading(order.UserId, sellOrder.UserId))
                        continue;

                    var remainingQty = order.Quantity - order.FilledQuantity;
                    var availableQty = sellOrder.Quantity - sellOrder.FilledQuantity;
                    var fillQty = Math.Min(remainingQty, availableQty);

                    if (fillQty <= 0) continue;

                    var fillPrice = sellOrder.Price; // Execute at seller's price (better for buyer)
                    var totalCost = fillQty * fillPrice;

                    // Execute trade
                    var trade = new Trade
                    {
                        BuyOrderId = order.Id,
                        SellOrderId = sellOrder.Id,
                        BuyerId = order.UserId,
                        SellerId = sellOrder.UserId,
                        Symbol = order.Symbol,
                        Quantity = fillQty,
                        Price = fillPrice,
                        Timestamp = DateTime.UtcNow
                    };

                    trades.Add(trade);
                    _db.Trades.Add(trade);

                    // Update buyer
                    order.FilledQuantity += fillQty;
                    if (!_db.Users[order.UserId].Portfolio.ContainsKey(order.Symbol))
                        _db.Users[order.UserId].Portfolio[order.Symbol] = 0;
                    _db.Users[order.UserId].Portfolio[order.Symbol] += fillQty;
                    _db.Users[order.UserId].TradeCount++;
                    _db.Users[order.UserId].TotalVolume += totalCost;

                    // Refund excess to buyer (bought at lower price)
                    var refund = fillQty * (order.Price - fillPrice);
                    _db.Users[order.UserId].Balance += refund;

                    // Update seller
                    sellOrder.FilledQuantity += fillQty;
                    _db.Users[sellOrder.UserId].Balance += fillQty * fillPrice;
                    _db.Users[sellOrder.UserId].Portfolio[order.Symbol] -= fillQty;
                    _db.Users[sellOrder.UserId].TradeCount++;
                    _db.Users[sellOrder.UserId].TotalVolume += totalCost;

                    // Update sell order status
                    if (sellOrder.FilledQuantity >= sellOrder.Quantity)
                        sellOrder.Status = "filled";
                    else
                        sellOrder.Status = "partial";

                    sellOrder.UpdatedAt = DateTime.UtcNow;

                    // Update coin stats
                    coin.Volume24h += totalCost;
                    coin.CurrentPrice = fillPrice;
                    coin.High24h = Math.Max(coin.High24h, fillPrice);
                    coin.Low24h = coin.Low24h == 0 ? fillPrice : Math.Min(coin.Low24h, fillPrice);

                    // Record price point
                    coin.PriceHistory.Add(new PricePoint
                    {
                        Timestamp = DateTime.UtcNow,
                        Price = fillPrice,
                        Volume = totalCost
                    });

                    // Trim price history to last 500 points
                    if (coin.PriceHistory.Count > 500)
                        coin.PriceHistory = coin.PriceHistory.TakeLast(500).ToList();
                }

                // Update buy order status
                if (order.FilledQuantity >= order.Quantity)
                {
                    order.Status = "filled";
                }
                else if (order.FilledQuantity > 0)
                {
                    order.Status = "partial";
                }
                else
                {
                    // No match, add to order book
                    order.Status = "open";
                }

                // Refund if no match
                if (order.Status == "rejected" || order.FilledQuantity == 0 && order.Status != "open")
                {
                    user.Balance += requiredBalance;
                }
            }
            else // sell
            {
                // Validate seller has enough coins
                if (!user.Portfolio.ContainsKey(order.Symbol) ||
                    user.Portfolio[order.Symbol] < order.Quantity)
                {
                    order.Status = "rejected";
                    _db.Orders.Add(order);
                    return trades;
                }

                // Reserve the coins
                user.Portfolio[order.Symbol] -= order.Quantity;

                // Match with existing buy orders (highest price first)
                var matchingBuys = _db.Orders
                    .Where(o => o.Type == "buy" && o.Symbol == order.Symbol &&
                                o.Status == "open" && o.Price >= order.Price)
                    .OrderByDescending(o => o.Price)
                    .ThenBy(o => o.CreatedAt)
                    .ToList();

                foreach (var buyOrder in matchingBuys)
                {
                    if (order.Status == "filled") break;

                    // Anti-cheat: check self-trading
                    if (_antiCheat.IsSelfTrading(order.UserId, buyOrder.UserId))
                        continue;

                    var remainingQty = order.Quantity - order.FilledQuantity;
                    var availableQty = buyOrder.Quantity - buyOrder.FilledQuantity;
                    var fillQty = Math.Min(remainingQty, availableQty);

                    if (fillQty <= 0) continue;

                    var fillPrice = buyOrder.Price; // Execute at buyer's price (better for seller)
                    var totalRevenue = fillQty * fillPrice;

                    // Execute trade
                    var trade = new Trade
                    {
                        BuyOrderId = buyOrder.Id,
                        SellOrderId = order.Id,
                        BuyerId = buyOrder.UserId,
                        SellerId = order.UserId,
                        Symbol = order.Symbol,
                        Quantity = fillQty,
                        Price = fillPrice,
                        Timestamp = DateTime.UtcNow
                    };

                    trades.Add(trade);
                    _db.Trades.Add(trade);

                    // Update seller
                    order.FilledQuantity += fillQty;
                    _db.Users[order.UserId].Balance += totalRevenue;
                    _db.Users[order.UserId].TradeCount++;
                    _db.Users[order.UserId].TotalVolume += totalRevenue;

                    // Update buyer
                    buyOrder.FilledQuantity += fillQty;
                    if (!_db.Users[buyOrder.UserId].Portfolio.ContainsKey(order.Symbol))
                        _db.Users[buyOrder.UserId].Portfolio[order.Symbol] = 0;
                    _db.Users[buyOrder.UserId].Portfolio[order.Symbol] += fillQty;

                    // Refund excess to buyer
                    var refund = fillQty * (buyOrder.Price - fillPrice);
                    _db.Users[buyOrder.UserId].Balance += refund;

                    _db.Users[buyOrder.UserId].TradeCount++;
                    _db.Users[buyOrder.UserId].TotalVolume += totalRevenue;

                    // Update buy order status
                    if (buyOrder.FilledQuantity >= buyOrder.Quantity)
                        buyOrder.Status = "filled";
                    else
                        buyOrder.Status = "partial";

                    buyOrder.UpdatedAt = DateTime.UtcNow;

                    // Update coin stats
                    coin.Volume24h += totalRevenue;
                    coin.CurrentPrice = fillPrice;
                    coin.High24h = Math.Max(coin.High24h, fillPrice);
                    coin.Low24h = coin.Low24h == 0 ? fillPrice : Math.Min(coin.Low24h, fillPrice);

                    coin.PriceHistory.Add(new PricePoint
                    {
                        Timestamp = DateTime.UtcNow,
                        Price = fillPrice,
                        Volume = totalRevenue
                    });

                    if (coin.PriceHistory.Count > 500)
                        coin.PriceHistory = coin.PriceHistory.TakeLast(500).ToList();
                }

                // Update sell order status
                if (order.FilledQuantity >= order.Quantity)
                {
                    order.Status = "filled";
                }
                else if (order.FilledQuantity > 0)
                {
                    order.Status = "partial";
                }
                else
                {
                    order.Status = "open";
                }

                // Return coins if no match
                if (order.Status == "rejected" || (order.FilledQuantity == 0 && order.Status != "open"))
                {
                    user.Portfolio[order.Symbol] += order.Quantity;
                }
            }

            _db.Orders.Add(order);

            // Update holder count
            coin.HolderCount = _db.Users.Values.Count(u => u.Portfolio.ContainsKey(order.Symbol) && u.Portfolio[order.Symbol] > 0);

            // Update market cap
            coin.MarketCap = coin.CurrentPrice * coin.CirculatingSupply;

            // Update 24h change
            Update24hChange(coin);

            return trades;
        }

        public bool CancelOrder(long userId, string orderId)
        {
            var order = _db.Orders.FirstOrDefault(o => o.Id == orderId && o.UserId == userId && (o.Status == "open" || o.Status == "partial"));
            if (order == null) return false;

            var user = _db.Users[userId];

            if (order.Type == "buy")
            {
                // Refund reserved balance
                var remainingQty = order.Quantity - order.FilledQuantity;
                user.Balance += remainingQty * order.Price;
            }
            else // sell
            {
                // Return reserved coins
                var remainingQty = order.Quantity - order.FilledQuantity;
                if (!user.Portfolio.ContainsKey(order.Symbol))
                    user.Portfolio[order.Symbol] = 0;
                user.Portfolio[order.Symbol] += remainingQty;
            }

            order.Status = "cancelled";
            order.UpdatedAt = DateTime.UtcNow;
            return true;
        }

        private void Update24hChange(Coin coin)
        {
            var cutoff = DateTime.UtcNow.AddHours(-24);
            var prices24h = coin.PriceHistory.Where(p => p.Timestamp >= cutoff).ToList();

            if (prices24h.Count >= 2)
            {
                var oldPrice = prices24h.First().Price;
                if (oldPrice > 0)
                    coin.Change24h = ((coin.CurrentPrice - oldPrice) / oldPrice) * 100;
            }
        }
    }

    // ========================================================================
    // Daily Reward System
    // ========================================================================

    public class DailyRewardSystem
    {
        private readonly BotDatabase _db;
        private readonly TelegramApi _api;
        private readonly int _ownerId;
        private bool _dailyProcessedToday = false;
        private DateTime _lastDailyCheck = DateTime.MinValue;

        public DailyRewardSystem(BotDatabase db, TelegramApi api, int ownerId)
        {
            _db = db;
            _api = api;
            _ownerId = ownerId;
        }

        public async Task CheckAndDistribute()
        {
            // Tehran time is UTC+3:30
            var tehranNow = DateTime.UtcNow.AddHours(3.5);

            // Check if it's a new day in Tehran time
            if (tehranNow.Hour == 0 && tehranNow.Minute < 5) // Check in the first 5 minutes of midnight
            {
                if (_lastDailyCheck.Date < tehranNow.Date)
                {
                    _lastDailyCheck = tehranNow;
                    await DistributeRewards();
                }
            }
        }

        private async Task DistributeRewards()
        {
            Console.WriteLine("[DailyReward] Distributing daily rewards...");
            int count = 0;

            foreach (var user in _db.Users.Values.ToList())
            {
                if (user.IsBanned || user.IsSuspended) continue;

                var totalValue = CalculateTotalValue(user);

                if (totalValue < 5000)
                {
                    // Check if already received today
                    if (user.LastDaily.HasValue &&
                        (DateTime.UtcNow.AddHours(3.5)).Date == user.LastDaily.Value.Date)
                        continue;

                    user.Balance += 1000;
                    user.LastDaily = DateTime.UtcNow;
                    count++;

                    try
                    {
                        await _api.SendMessage(user.Id,
                            $"🎁 <b>جایزه روزانه</b>\n\n" +
                            $"موجودی شما کمتر از ۵,۰۰۰ بود، پس ۱,۰۰۰ سکه به عنوان جایزه روزانه دریافت کردید!\n\n" +
                            $"💰 موجودی جدید: <b>{user.Balance:N0}</b> سکه\n\n" +
                            $"ارزش کل دارایی شما: <b>{CalculateTotalValue(user):N0}</b> سکه");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[DailyReward] Failed to notify user {user.Id}: {ex.Message}");
                    }
                }
            }

            Console.WriteLine($"[DailyReward] Distributed rewards to {count} users");
        }

        public decimal CalculateTotalValue(User user)
        {
            decimal total = user.Balance;
            foreach (var kvp in user.Portfolio)
            {
                if (_db.Coins.ContainsKey(kvp.Key))
                {
                    total += kvp.Value * _db.Coins[kvp.Key].CurrentPrice;
                }
            }
            return total;
        }
    }

    // ========================================================================
    // Database Manager
    // ========================================================================

    public class DatabaseManager
    {
        private readonly string _filePath;
        private readonly object _lock = new();
        private BotDatabase _db;

        public DatabaseManager(string filePath)
        {
            _filePath = filePath;
            _db = Load();
        }

        public BotDatabase Db => _db;

        public BotDatabase Load()
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    var json = File.ReadAllText(_filePath);
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    _db = JsonSerializer.Deserialize<BotDatabase>(json, options) ?? new BotDatabase();
                    Console.WriteLine($"[DB] Loaded database from {_filePath}");
                }
                else
                {
                    _db = new BotDatabase();
                    Console.WriteLine("[DB] Created new database");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DB] Error loading: {ex.Message}");
                _db = new BotDatabase();
            }
            return _db;
        }

        public void Save()
        {
            lock (_lock)
            {
                try
                {
                    var options = new JsonSerializerOptions { WriteIndented = true };
                    var json = JsonSerializer.Serialize(_db, options);
                    File.WriteAllText(_filePath, json);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DB] Error saving: {ex.Message}");
                }
            }
        }
    }

    // ========================================================================
    // Main Bot Controller
    // ========================================================================

    public class SahamBotController
    {
        // Config
        private const string BOT_TOKEN = "8871928516:AAGChm-ApCPvd53KZDD8pIr1CEfYISCcLqI";
        private const int OWNER_ID = 8248899977;
        private const decimal INITIAL_BALANCE = 5000;
        private const decimal DAILY_REWARD_THRESHOLD = 5000;
        private const decimal DAILY_REWARD_AMOUNT = 1000;

        private readonly TelegramApi _api;
        private readonly DatabaseManager _dbManager;
        private readonly AntiCheatSystem _antiCheat;
        private readonly MatchingEngine _matchingEngine;
        private readonly DailyRewardSystem _dailyReward;
        private readonly ConcurrentDictionary<long, UserSession> _sessions = new();
        private int _lastUpdateId = 0;

        public SahamBotController()
        {
            _api = new TelegramApi(BOT_TOKEN);
            _dbManager = new DatabaseManager("saham_db.json");
            _antiCheat = new AntiCheatSystem(_dbManager.Db);
            _matchingEngine = new MatchingEngine(_dbManager.Db, _antiCheat);
            _dailyReward = new DailyRewardSystem(_dbManager.Db, _api, OWNER_ID);
        }

        public async Task Run()
        {
            Console.WriteLine("╔══════════════════════════════════════════╗");
            Console.WriteLine("║   🏦 SahamBot - Stock Market Bot       ║");
            Console.WriteLine("║   Version: 2.0.0                        ║");
            Console.WriteLine("║   Single-File C# Telegram Bot           ║");
            Console.WriteLine("╚══════════════════════════════════════════╝");

            // Verify bot token
            var me = await _api.GetMe();
            if (me.HasValue)
            {
                Console.WriteLine($"[Bot] Connected as: {me.Value.GetProperty("result").GetProperty("first_name").GetString()}");
            }
            else
            {
                Console.WriteLine("[Bot] Warning: Could not verify bot token");
            }

            Console.WriteLine($"[Bot] Owner ID: {OWNER_ID}");
            Console.WriteLine($"[Bot] Users: {_dbManager.Db.Users.Count}");
            Console.WriteLine($"[Bot] Coins: {_dbManager.Db.Coins.Count}");
            Console.WriteLine("[Bot] Listening for updates...\n");

            // Auto-save timer
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    await Task.Delay(TimeSpan.FromMinutes(2));
                    _dbManager.Save();
                }
            });

            // Daily reward timer - check every 30 seconds
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    await Task.Delay(TimeSpan.FromSeconds(30));
                    try { await _dailyReward.CheckAndDistribute(); }
                    catch (Exception ex) { Console.WriteLine($"[Daily] Error: {ex.Message}"); }
                }
            });

            // Periodic 24h stats reset (every hour)
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    await Task.Delay(TimeSpan.FromHours(1));
                    Reset24hStats();
                }
            });

            // Main polling loop
            while (true)
            {
                try
                {
                    var updates = await _api.GetUpdates(_lastUpdateId + 1, 30);
                    if (updates.HasValue && updates.Value.TryGetProperty("result", out var results))
                    {
                        foreach (var update in results.EnumerateArray())
                        {
                            _lastUpdateId = update.GetProperty("update_id").GetInt32();
                            try
                            {
                                await ProcessUpdate(update);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[Error] Processing update: {ex.Message}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Poll Error] {ex.Message}");
                    await Task.Delay(5000);
                }
            }
        }

        private void Reset24hStats()
        {
            foreach (var coin in _dbManager.Db.Coins.Values)
            {
                var cutoff = DateTime.UtcNow.AddHours(-24);
                var recentTrades = _dbManager.Db.Trades
                    .Where(t => t.Symbol == coin.Symbol && t.Timestamp >= cutoff)
                    .ToList();

                coin.Volume24h = recentTrades.Sum(t => t.Quantity * t.Price);
                coin.High24h = recentTrades.Count > 0 ? recentTrades.Max(t => t.Price) : coin.CurrentPrice;
                coin.Low24h = recentTrades.Count > 0 ? recentTrades.Min(t => t.Price) : coin.CurrentPrice;
                coin.HolderCount = _dbManager.Db.Users.Values.Count(u => u.Portfolio.ContainsKey(coin.Symbol) && u.Portfolio[coin.Symbol] > 0);
            }
        }

        private async Task ProcessUpdate(JsonElement update)
        {
            if (update.TryGetProperty("message", out var message))
            {
                await ProcessMessage(message);
            }
            else if (update.TryGetProperty("callback_query", out var callback))
            {
                await ProcessCallback(callback);
            }
        }

        private async Task ProcessMessage(JsonElement message)
        {
            var chatId = message.GetProperty("chat").GetProperty("id").GetInt64();
            var userId = message.GetProperty("from").GetProperty("id").GetInt64();
            var firstName = message.TryGetProperty("from", out var from) && from.TryGetProperty("first_name", out var fn) ? fn.GetString() ?? "" : "";
            var username = from.TryGetProperty("username", out var un) ? un.GetString() ?? "" : "";

            // Check if banned
            if (_dbManager.Db.BannedUsers.Contains(userId))
            {
                await _api.SendMessage(chatId, "⛔ شما از بات بن شده‌اید.");
                return;
            }

            // Auto-register
            if (!_dbManager.Db.Users.ContainsKey(userId))
            {
                var newUser = new User
                {
                    Id = userId,
                    Username = username,
                    FirstName = firstName,
                    Balance = INITIAL_BALANCE,
                    RegisteredAt = DateTime.UtcNow,
                    LastActive = DateTime.UtcNow
                };
                _dbManager.Db.Users[userId] = newUser;
                _dbManager.Save();

                await _api.SendMessage(chatId,
                    $"👋 <b>خوش آمدید به بازار سهام!</b>\n\n" +
                    $"💰 موجودی اولیه شما: <b>{INITIAL_BALANCE:N0}</b> سکه\n\n" +
                    $"📋 برای شروع از منوی زیر استفاده کنید:\n" +
                    $"▫️ /market - مشاهده بازار\n" +
                    $"▫️ /portfolio - دارایی‌های شما\n" +
                    $"▫️ /menu - منوی اصلی\n\n" +
                    $"⚠️ توجه: استفاده از چند اکانت ممنوع است و منجر به بن خواهد شد.");
            }

            // Update user activity
            if (_dbManager.Db.Users.ContainsKey(userId))
            {
                _dbManager.Db.Users[userId].Username = username;
                _dbManager.Db.Users[userId].FirstName = firstName;
                _dbManager.Db.Users[userId].LastActive = DateTime.UtcNow;
            }

            // Generate fingerprint from user data
            var fingerprint = $"{userId}_{firstName}_{username}";
            _antiCheat.UpdateFingerprint(userId, fingerprint);

            // Get text
            var text = message.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
            if (string.IsNullOrEmpty(text)) return;

            // Check session state
            if (_sessions.TryGetValue(userId, out var session) && session.State != "idle")
            {
                await HandleSessionInput(userId, chatId, text, session);
                return;
            }

            // Process commands
            var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var command = parts[0].ToLower();

            switch (command)
            {
                case "/start":
                    await HandleStart(chatId, userId);
                    break;
                case "/menu":
                    await HandleMenu(chatId, userId);
                    break;
                case "/market":
                    await HandleMarket(chatId);
                    break;
                case "/portfolio":
                case "/wallet":
                    await HandlePortfolio(chatId, userId);
                    break;
                case "/balance":
                    await HandleBalance(chatId, userId);
                    break;
                case "/trade":
                    if (parts.Length > 1) await HandleTrade(chatId, userId, parts[1].ToUpper());
                    else await HandleMarket(chatId);
                    break;
                case "/buy":
                    if (parts.Length >= 4) await HandleBuy(chatId, userId, parts[1].ToUpper(), parts[2], parts[3]);
                    else await StartBuyFlow(chatId, userId);
                    break;
                case "/sell":
                    if (parts.Length >= 4) await HandleSell(chatId, userId, parts[1].ToUpper(), parts[2], parts[3]);
                    else await StartSellFlow(chatId, userId);
                    break;
                case "/cancel":
                    if (parts.Length > 1) await HandleCancelOrder(chatId, userId, parts[1]);
                    else await HandleOpenOrders(chatId, userId);
                    break;
                case "/orders":
                    await HandleOpenOrders(chatId, userId);
                    break;
                case "/chart":
                    if (parts.Length > 1) await HandleChart(chatId, parts[1].ToUpper());
                    else await HandleMarket(chatId);
                    break;
                case "/history":
                    if (parts.Length > 1) await HandleHistory(chatId, parts[1].ToUpper());
                    else await HandleMarket(chatId);
                    break;
                case "/orderbook":
                    if (parts.Length > 1) await HandleOrderBook(chatId, parts[1].ToUpper());
                    else await HandleMarket(chatId);
                    break;
                case "/leaderboard":
                case "/top":
                    await HandleLeaderboard(chatId);
                    break;
                case "/daily":
                    await HandleDailyStatus(chatId, userId);
                    break;
                case "/help":
                    await HandleHelp(chatId);
                    break;
                case "/stats":
                    await HandleStats(chatId);
                    break;
                case "/profile":
                    await HandleProfile(chatId, userId);
                    break;
                // Admin commands
                case "/admin":
                    await HandleAdmin(chatId, userId);
                    break;
                case "/addcoin":
                    await HandleAddCoin(chatId, userId);
                    break;
                case "/setprice":
                    if (parts.Length > 2) await HandleSetPrice(chatId, userId, parts[1].ToUpper(), parts[2]);
                    break;
                case "/setuserbal":
                    if (parts.Length > 2) await HandleSetUserBalance(chatId, userId, parts[1], parts[2]);
                    break;
                case "/ban":
                    if (parts.Length > 1) await HandleBan(chatId, userId, parts[1]);
                    break;
                case "/unban":
                    if (parts.Length > 1) await HandleUnban(chatId, userId, parts[1]);
                    break;
                case "/broadcast":
                    if (text.Length > 11) await HandleBroadcast(chatId, userId, text[11..]);
                    else await StartBroadcastFlow(chatId, userId);
                    break;
                case "/userlist":
                    await HandleUserList(chatId, userId);
                    break;
                case "/userinfo":
                    if (parts.Length > 1) await HandleUserInfo(chatId, userId, parts[1]);
                    break;
                case "/suspend":
                    if (parts.Length > 1) await HandleSuspendCoin(chatId, userId, parts[1].ToUpper());
                    break;
                case "/unsuspend":
                    if (parts.Length > 1) await HandleUnsuspendCoin(chatId, userId, parts[1].ToUpper());
                    break;
                case "/resetbal":
                    if (parts.Length > 1) await HandleResetBalance(chatId, userId, parts[1]);
                    break;
                case "/forcebuy":
                    if (parts.Length >= 4) await HandleForceOrder(chatId, userId, "buy", parts[1].ToUpper(), parts[2], parts[3]);
                    break;
                case "/forcesell":
                    if (parts.Length >= 4) await HandleForceOrder(chatId, userId, "sell", parts[1].ToUpper(), parts[2], parts[3]);
                    break;
                case "/deldaily":
                    await HandleManualDaily(chatId, userId);
                    break;
                case "/globalstats":
                    await HandleGlobalStats(chatId, userId);
                    break;
                case "/flagged":
                    await HandleFlaggedUsers(chatId, userId);
                    break;
                default:
                    // Check for unknown command
                    if (text.StartsWith("/"))
                    {
                        await _api.SendMessage(chatId, "❓ دستور ناشناخته!\nاز /menu برای مشاهده دستورات استفاده کنید.");
                    }
                    break;
            }
        }

        // ====================================================================
        // User Commands
        // ====================================================================

        private async Task HandleStart(long chatId, long userId)
        {
            var user = _dbManager.Db.Users[userId];
            var totalValue = _dailyReward.CalculateTotalValue(user);

            var keyboard = new InlineKeyboard()
                .AddButton("📊 بازار", "cmd_market")
                .AddButton("💼 دارایی", "cmd_portfolio")
                .NewRow()
                .AddButton("📈 نمودار", "cmd_chart_menu")
                .AddButton("🏆 برترین‌ها", "cmd_leaderboard")
                .NewRow()
                .AddButton("❓ راهنما", "cmd_help")
                .NewRow()
                .AddButton("🔄 آپدیت", "cmd_refresh_main")
                .Build();

            await _api.SendMessage(chatId,
                $"🏦 <b>بازار سهام SahamBot</b>\n\n" +
                $"━━━━━━━━━━━━━━━━━━\n" +
                $"👤 {GetDisplayName(user)}\n" +
                $"💰 موجودی: <b>{user.Balance:N0}</b> سکه\n" +
                $"📊 ارزش کل: <b>{totalValue:N0}</b> سکه\n" +
                $"📅 عضویت: {user.RegisteredAt:yyyy/MM/dd}\n" +
                $"━━━━━━━━━━━━━━━━━━\n\n" +
                $"🎁 جایزه روزانه: کاربرانی با دارایی کمتر از ۵,۰۰۰\n" +
                $"   هر شب ساعت ۲۴ (تهران) ۱,۰۰۰ سکه دریافت می‌کنند!\n\n" +
                $"⚠️ <b>هشدار:</b> استفاده از چند اکانت ممنوع است!",
                replyMarkup: keyboard);
        }

        private async Task HandleMenu(long chatId, long userId)
        {
            var user = _dbManager.Db.Users[userId];
            var totalValue = _dailyReward.CalculateTotalValue(user);

            var keyboard = new InlineKeyboard()
                .AddButton("📊 بازار سهام", "cmd_market")
                .AddButton("💼 کیف پول", "cmd_portfolio")
                .NewRow()
                .AddButton("🛒 خرید", "cmd_buy_menu")
                .AddButton("💸 فروش", "cmd_sell_menu")
                .NewRow()
                .AddButton("📋 سفارش‌های باز", "cmd_orders")
                .AddButton("📈 نمودار", "cmd_chart_menu")
                .NewRow()
                .AddButton("🏆 رتبه‌بندی", "cmd_leaderboard")
                .AddButton("👤 پروفایل", "cmd_profile")
                .NewRow()
                .AddButton("🎁 جایزه روزانه", "cmd_daily")
                .AddButton("📊 آمار", "cmd_stats")
                .NewRow()
                .AddButton("❓ راهنما", "cmd_help")
                .Build();

            await _api.SendMessage(chatId,
                $"📋 <b>منوی اصلی</b>\n\n" +
                $"💰 موجودی: <b>{user.Balance:N0}</b>\n" +
                $"📊 ارزش کل دارایی: <b>{totalValue:N0}</b>",
                replyMarkup: keyboard);
        }

        private async Task HandleMarket(long chatId)
        {
            var coins = _dbManager.Db.Coins.Values.Where(c => c.IsActive).ToList();

            if (coins.Count == 0)
            {
                await _api.SendMessage(chatId, "📊 بازار خالی است!\nهنوز ارزی اضافه نشده.");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("📊 <b>بازار سهام</b>\n");
            sb.AppendLine("━━━━━━━━━━━━━━━━━━");

            foreach (var coin in coins)
            {
                var change = coin.Change24h;
                var changeEmoji = change >= 0 ? "🟢" : "🔴";
                var changeStr = change >= 0 ? $"+{change:F1}%" : $"{change:F1}%";
                var miniChart = ChartGenerator.GenerateMiniChart(coin.PriceHistory.Select(p => p.Price).ToList());

                sb.AppendLine($"{changeEmoji} <b>{coin.Symbol}</b> | {coin.Name}");
                sb.AppendLine($"   💎 قیمت: <b>{coin.CurrentPrice:N0}</b> | {changeStr}");
                sb.AppendLine($"   📊 حجم ۲۴h: {coin.Volume24h:N0} | {miniChart}");
                sb.AppendLine($"   👥 دارندگان: {coin.HolderCount}");
                sb.AppendLine();
            }

            sb.AppendLine("━━━━━━━━━━━━━━━━━━");
            sb.AppendLine("📌 برای معامله: /trade SYMBOL");
            sb.AppendLine("📌 برای نمودار: /chart SYMBOL");

            var keyboard = new InlineKeyboard();
            foreach (var coin in coins.Take(8))
            {
                keyboard.AddButton($"{coin.Symbol}", $"cmd_trade_{coin.Symbol}");
                if (coins.IndexOf(coin) % 3 == 2 || coins.IndexOf(coin) == coins.Count - 1)
                    keyboard.NewRow();
            }
            keyboard.AddButton("🔄 بروزرسانی", "cmd_market").NewRow();

            await _api.SendMessage(chatId, sb.ToString(), replyMarkup: keyboard.Build());
        }

        private async Task HandlePortfolio(long chatId, long userId)
        {
            if (!_dbManager.Db.Users.ContainsKey(userId)) return;
            var user = _dbManager.Db.Users[userId];
            var totalValue = _dailyReward.CalculateTotalValue(user);

            var sb = new StringBuilder();
            sb.AppendLine("💼 <b>کیف پول شما</b>\n");
            sb.AppendLine("━━━━━━━━━━━━━━━━━━");
            sb.AppendLine($"💰 موجودی نقد: <b>{user.Balance:N0}</b> سکه");
            sb.AppendLine();

            if (user.Portfolio.Count == 0 || user.Portfolio.All(p => p.Value == 0))
            {
                sb.AppendLine("📭 هیچ دارایی ندارید");
            }
            else
            {
                sb.AppendLine("<b>دارایی‌ها:</b>\n");
                foreach (var kvp in user.Portfolio.Where(p => p.Value > 0))
                {
                    if (_dbManager.Db.Coins.ContainsKey(kvp.Key))
                    {
                        var coin = _dbManager.Db.Coins[kvp.Key];
                        var value = kvp.Value * coin.CurrentPrice;
                        var pnl = value - (kvp.Value * coin.BaseValue);
                        var pnlEmoji = pnl >= 0 ? "🟢" : "🔴";
                        var pnlStr = pnl >= 0 ? $"+{pnl:N0}" : $"{pnl:N0}";

                        sb.AppendLine($"  {coin.Symbol} | {kvp.Value:N0} واحد");
                        sb.AppendLine($"  قیمت: {coin.CurrentPrice:N0} | ارزش: {value:N0}");
                        sb.AppendLine($"  {pnlEmoji} سود/زیان: {pnlStr}");
                        sb.AppendLine();
                    }
                }
            }

            sb.AppendLine("━━━━━━━━━━━━━━━━━━");
            sb.AppendLine($"📊 ارزش کل: <b>{totalValue:N0}</b> سکه");

            if (totalValue >= INITIAL_BALANCE)
            {
                var profit = totalValue - INITIAL_BALANCE;
                sb.AppendLine($"🎉 سود کل: <b>+{profit:N0}</b> ({(profit / INITIAL_BALANCE * 100):F1}%)");
            }
            else
            {
                var loss = INITIAL_BALANCE - totalValue;
                sb.AppendLine($"📉 زیان کل: <b>{loss:N0}</b> ({(loss / INITIAL_BALANCE * 100):F1}%)");
            }

            var keyboard = new InlineKeyboard()
                .AddButton("🛒 خرید", "cmd_buy_menu")
                .AddButton("💸 فروش", "cmd_sell_menu")
                .NewRow()
                .AddButton("🔙 منو", "cmd_menu")
                .Build();

            await _api.SendMessage(chatId, sb.ToString(), replyMarkup: keyboard);
        }

        private async Task HandleBalance(long chatId, long userId)
        {
            if (!_dbManager.Db.Users.ContainsKey(userId)) return;
            var user = _dbManager.Db.Users[userId];
            var totalValue = _dailyReward.CalculateTotalValue(user);

            await _api.SendMessage(chatId,
                $"💰 <b>موجودی شما</b>\n\n" +
                $"💵 نقد: <b>{user.Balance:N0}</b> سکه\n" +
                $"📊 ارزش کل دارایی: <b>{totalValue:N0}</b> سکه\n\n" +
                $"🎯 فاصله تا جایزه: {(totalValue < DAILY_REWARD_THRESHOLD ? $"<b>{(DAILY_REWARD_THRESHOLD - totalValue):N0}</b> تا ۵,۰۰۰" : "✅ بالاتر از حد")}");
        }

        private async Task HandleTrade(long chatId, long userId, string symbol)
        {
            if (!_dbManager.Db.Coins.ContainsKey(symbol))
            {
                await _api.SendMessage(chatId, $"❌ ارز {symbol} یافت نشد!");
                return;
            }

            var coin = _dbManager.Db.Coins[symbol];

            if (coin.IsSuspended)
            {
                await _api.SendMessage(chatId, $"⚠️ ارز {symbol} معلق شده و قابل معامله نیست.");
                return;
            }

            var user = _dbManager.Db.Users[userId];
            var holdingQty = user.Portfolio.ContainsKey(symbol) ? user.Portfolio[symbol] : 0;
            var holdingValue = holdingQty * coin.CurrentPrice;

            // Recent trades
            var recentTrades = _dbManager.Db.Trades
                .Where(t => t.Symbol == symbol)
                .OrderByDescending(t => t.Timestamp)
                .Take(5)
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"📊 <b>{coin.Symbol} - {coin.Name}</b>\n");
            sb.AppendLine("━━━━━━━━━━━━━━━━━━");
            sb.AppendLine($"💎 قیمت فعلی: <b>{coin.CurrentPrice:N0}</b>");

            var changeEmoji = coin.Change24h >= 0 ? "🟢" : "🔴";
            var changeStr = coin.Change24h >= 0 ? $"+{coin.Change24h:F2}%" : $"{coin.Change24h:F2}%";
            sb.AppendLine($"{changeEmoji} تغییر ۲۴h: {changeStr}");
            sb.AppendLine($"📈 بیشترین ۲۴h: {coin.High24h:N0}");
            sb.AppendLine($"📉 کمترین ۲۴h: {coin.Low24h:N0}");
            sb.AppendLine($"📊 حجم ۲۴h: {coin.Volume24h:N0}");
            sb.AppendLine($"💰 ارزش پایه: {coin.BaseValue:N0}");
            sb.AppendLine($"🪙 عرضه: {coin.CirculatingSupply:N0} / {coin.TotalSupply:N0}");
            sb.AppendLine($"📊 مارکت‌کپ: {coin.MarketCap:N0}");
            sb.AppendLine($"👥 دارندگان: {coin.HolderCount}");
            sb.AppendLine();
            sb.AppendLine($"💼 دارایی شما: <b>{holdingQty:N0}</b> واحد ({holdingValue:N0})");

            if (recentTrades.Count > 0)
            {
                sb.AppendLine($"\n<b>آخرین معاملات:</b>");
                foreach (var trade in recentTrades)
                {
                    sb.AppendLine($"  ▫️ {trade.Quantity:N0} × {trade.Price:N0} ({trade.Timestamp:HH:mm})");
                }
            }

            sb.AppendLine("━━━━━━━━━━━━━━━━━━");

            var keyboard = new InlineKeyboard()
                .AddButton("🛒 خرید", $"cmd_buy_{symbol}")
                .AddButton("💸 فروش", $"cmd_sell_{symbol}")
                .NewRow()
                .AddButton("📈 نمودار", $"cmd_chart_{symbol}")
                .AddButton("📋 دفتر سفارش", $"cmd_orderbook_{symbol}")
                .NewRow()
                .AddButton("🔄 بروزرسانی", $"cmd_trade_{symbol}")
                .AddButton("🔙 بازار", "cmd_market")
                .Build();

            await _api.SendMessage(chatId, sb.ToString(), replyMarkup: keyboard);
        }

        private async Task HandleBuy(long chatId, long userId, string symbol, string qtyStr, string priceStr)
        {
            if (!decimal.TryParse(qtyStr, out var qty) || qty <= 0)
            {
                await _api.SendMessage(chatId, "❌ مقدار نامعتبر!");
                return;
            }
            if (!decimal.TryParse(priceStr, out var price) || price <= 0)
            {
                await _api.SendMessage(chatId, "❌ قیمت نامعتبر!");
                return;
            }

            if (!_dbManager.Db.Coins.ContainsKey(symbol))
            {
                await _api.SendMessage(chatId, $"❌ ارز {symbol} یافت نشد!");
                return;
            }

            var user = _dbManager.Db.Users[userId];
            var totalCost = qty * price;

            if (user.Balance < totalCost)
            {
                await _api.SendMessage(chatId,
                    $"❌ موجودی کافی نیست!\n" +
                    $"💰 مورد نیاز: <b>{totalCost:N0}</b>\n" +
                    $"💵 موجودی: <b>{user.Balance:N0}</b>");
                return;
            }

            var order = new Order
            {
                UserId = userId,
                Type = "buy",
                Symbol = symbol,
                Quantity = qty,
                Price = price
            };

            var trades = _matchingEngine.PlaceOrder(order);
            _dbManager.Save();

            if (trades.Count > 0)
            {
                var filledQty = trades.Sum(t => t.Quantity);
                var avgPrice = trades.Sum(t => t.Quantity * t.Price) / filledQty;

                await _api.SendMessage(chatId,
                    $"✅ <b>سفارش خرید انجام شد!</b>\n\n" +
                    $"🛒 {symbol}: <b>{filledQty:N0}</b> واحد × <b>{avgPrice:N0}</b>\n" +
                    $"💰 هزینه: <b>{filledQty * avgPrice:N0}</b> سکه\n" +
                    $"📋 تعداد معاملات: {trades.Count}\n\n" +
                    $"{(order.Status == "partial" ? $"⚠️ بخشی از سفارش باز ماند: {order.Quantity - order.FilledQuantity:N0} واحد" : "")}");
            }
            else if (order.Status == "open")
            {
                await _api.SendMessage(chatId,
                    $"📋 <b>سفارش ثبت شد</b>\n\n" +
                    $"🛒 خرید {symbol}: <b>{qty:N0}</b> واحد @ <b>{price:N0}</b>\n" +
                    $"💰 رزرو: <b>{totalCost:N0}</b> سکه\n" +
                    $"🔑 کد: <code>{order.Id}</code>\n\n" +
                    $"⏳ در انتظار فروشنده...");
            }
            else
            {
                await _api.SendMessage(chatId, $"❌ سفارش رد شد. لطفاً دوباره تلاش کنید.");
            }
        }

        private async Task HandleSell(long chatId, long userId, string symbol, string qtyStr, string priceStr)
        {
            if (!decimal.TryParse(qtyStr, out var qty) || qty <= 0)
            {
                await _api.SendMessage(chatId, "❌ مقدار نامعتبر!");
                return;
            }
            if (!decimal.TryParse(priceStr, out var price) || price <= 0)
            {
                await _api.SendMessage(chatId, "❌ قیمت نامعتبر!");
                return;
            }

            if (!_dbManager.Db.Coins.ContainsKey(symbol))
            {
                await _api.SendMessage(chatId, $"❌ ارز {symbol} یافت نشد!");
                return;
            }

            var user = _dbManager.Db.Users[userId];
            var holding = user.Portfolio.ContainsKey(symbol) ? user.Portfolio[symbol] : 0;

            if (holding < qty)
            {
                await _api.SendMessage(chatId,
                    $"❌ موجودی کافی نیست!\n" +
                    $"🪙 نیاز: <b>{qty:N0}</b> {symbol}\n" +
                    $"💼 دارید: <b>{holding:N0}</b> {symbol}");
                return;
            }

            var order = new Order
            {
                UserId = userId,
                Type = "sell",
                Symbol = symbol,
                Quantity = qty,
                Price = price
            };

            var trades = _matchingEngine.PlaceOrder(order);
            _dbManager.Save();

            if (trades.Count > 0)
            {
                var filledQty = trades.Sum(t => t.Quantity);
                var avgPrice = trades.Sum(t => t.Quantity * t.Price) / filledQty;

                await _api.SendMessage(chatId,
                    $"✅ <b>سفارش فروش انجام شد!</b>\n\n" +
                    $"💸 {symbol}: <b>{filledQty:N0}</b> واحد × <b>{avgPrice:N0}</b>\n" +
                    $"💰 درآمد: <b>{filledQty * avgPrice:N0}</b> سکه\n" +
                    $"📋 تعداد معاملات: {trades.Count}");
            }
            else if (order.Status == "open")
            {
                await _api.SendMessage(chatId,
                    $"📋 <b>سفارش ثبت شد</b>\n\n" +
                    $"💸 فروش {symbol}: <b>{qty:N0}</b> واحد @ <b>{price:N0}</b>\n" +
                    $"🔑 کد: <code>{order.Id}</code>\n\n" +
                    $"⏳ در انتظار خریدار...");
            }
            else
            {
                await _api.SendMessage(chatId, $"❌ سفارش رد شد. لطفاً دوباره تلاش کنید.");
            }
        }

        private async Task HandleCancelOrder(long chatId, long userId, string orderId)
        {
            if (_matchingEngine.CancelOrder(userId, orderId))
            {
                _dbManager.Save();
                await _api.SendMessage(chatId, $"✅ سفارش <code>{orderId}</code> لغو شد.");
            }
            else
            {
                await _api.SendMessage(chatId, $"❌ سفارش یافت نشد یا قابل لغو نیست.");
            }
        }

        private async Task HandleOpenOrders(long chatId, long userId)
        {
            var openOrders = _dbManager.Db.Orders
                .Where(o => o.UserId == userId && (o.Status == "open" || o.Status == "partial"))
                .OrderByDescending(o => o.CreatedAt)
                .Take(20)
                .ToList();

            if (openOrders.Count == 0)
            {
                await _api.SendMessage(chatId, "📋 هیچ سفارش بازی ندارید.");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("📋 <b>سفارش‌های باز شما</b>\n");
            sb.AppendLine("━━━━━━━━━━━━━━━━━━");

            foreach (var order in openOrders)
            {
                var emoji = order.Type == "buy" ? "🛒" : "💸";
                var remaining = order.Quantity - order.FilledQuantity;
                sb.AppendLine($"{emoji} <code>{order.Id}</code> | {order.Type == "buy" ? "خرید" : "فروش"} {order.Symbol}");
                sb.AppendLine($"   {remaining:N0}/{order.Quantity:N0} @ {order.Price:N0}");
                sb.AppendLine($"   ⏰ {order.CreatedAt:MM/dd HH:mm}");
                sb.AppendLine();
            }

            sb.AppendLine("━━━━━━━━━━━━━━━━━━");
            sb.AppendLine("📌 لغو: /cancel CODE");

            var keyboard = new InlineKeyboard();
            foreach (var order in openOrders.Take(6))
            {
                keyboard.AddButton($"❌ {order.Id}", $"cmd_cancel_{order.Id}");
                keyboard.NewRow();
            }
            keyboard.AddButton("🔄 بروزرسانی", "cmd_orders");

            await _api.SendMessage(chatId, sb.ToString(), replyMarkup: keyboard.Build());
        }

        private async Task HandleChart(long chatId, string symbol)
        {
            if (!_dbManager.Db.Coins.ContainsKey(symbol))
            {
                await _api.SendMessage(chatId, $"❌ ارز {symbol} یافت نشد!");
                return;
            }

            var coin = _dbManager.Db.Coins[symbol];
            var chart = ChartGenerator.GenerateAsciiChart(coin);

            var changeEmoji = coin.Change24h >= 0 ? "🟢" : "🔴";
            var changeStr = coin.Change24h >= 0 ? $"+{coin.Change24h:F2}%" : $"{coin.Change24h:F2}%";

            var keyboard = new InlineKeyboard()
                .AddButton("🛒 خرید", $"cmd_buy_{symbol}")
                .AddButton("💸 فروش", $"cmd_sell_{symbol}")
                .NewRow()
                .AddButton("📋 دفتر سفارش", $"cmd_orderbook_{symbol}")
                .AddButton("🔄 بروزرسانی", $"cmd_chart_{symbol}")
                .NewRow()
                .AddButton("🔙 بازار", "cmd_market")
                .Build();

            await _api.SendMessage(chatId,
                $"{chart}\n\n" +
                $"💎 قیمت: <b>{coin.CurrentPrice:N0}</b> | {changeEmoji} {changeStr}\n" +
                $"📈 High: {coin.High24h:N0} | 📉 Low: {coin.Low24h:N0}",
                replyMarkup: keyboard);
        }

        private async Task HandleHistory(long chatId, string symbol)
        {
            if (!_dbManager.Db.Coins.ContainsKey(symbol))
            {
                await _api.SendMessage(chatId, $"❌ ارز {symbol} یافت نشد!");
                return;
            }

            var coin = _dbManager.Db.Coins[symbol];
            var recentTrades = _dbManager.Db.Trades
                .Where(t => t.Symbol == symbol)
                .OrderByDescending(t => t.Timestamp)
                .Take(15)
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"📜 <b>تاریخچه معاملات {symbol}</b>\n");
            sb.AppendLine("━━━━━━━━━━━━━━━━━━");

            if (recentTrades.Count == 0)
            {
                sb.AppendLine("هیچ معامله‌ای ثبت نشده.");
            }
            else
            {
                foreach (var trade in recentTrades)
                {
                    var emoji = trade.Price >= coin.BaseValue ? "🟢" : "🔴";
                    sb.AppendLine($"{emoji} {trade.Quantity:N0} × {trade.Price:N0} ({trade.Timestamp:MM/dd HH:mm})");
                }
            }

            await _api.SendMessage(chatId, sb.ToString());
        }

        private async Task HandleOrderBook(long chatId, string symbol)
        {
            if (!_dbManager.Db.Coins.ContainsKey(symbol))
            {
                await _api.SendMessage(chatId, $"❌ ارز {symbol} یافت نشد!");
                return;
            }

            var coin = _dbManager.Db.Coins[symbol];

            var buyOrders = _dbManager.Db.Orders
                .Where(o => o.Type == "buy" && o.Symbol == symbol && (o.Status == "open" || o.Status == "partial"))
                .OrderByDescending(o => o.Price)
                .ThenBy(o => o.CreatedAt)
                .Take(10)
                .ToList();

            var sellOrders = _dbManager.Db.Orders
                .Where(o => o.Type == "sell" && o.Symbol == symbol && (o.Status == "open" || o.Status == "partial"))
                .OrderBy(o => o.Price)
                .ThenBy(o => o.CreatedAt)
                .Take(10)
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"📋 <b>دفتر سفارش {symbol}</b>");
            sb.AppendLine($"💎 قیمت: <b>{coin.CurrentPrice:N0}</b>\n");

            // Buy side
            sb.AppendLine("<b>🟢 خرید (Bid)</b>");
            sb.AppendLine("قیمت       │ تعداد       │ کل");
            sb.AppendLine("───────────┼────────────┼──────────");
            foreach (var order in buyOrders)
            {
                var remaining = order.Quantity - order.FilledQuantity;
                sb.AppendLine($"{order.Price,10:N0} │ {remaining,10:N0} │ {remaining * order.Price,10:N0}");
            }
            if (buyOrders.Count == 0) sb.AppendLine("   سفارشی نیست");

            sb.AppendLine();

            // Sell side
            sb.AppendLine("<b>🔴 فروش (Ask)</b>");
            sb.AppendLine("قیمت       │ تعداد       │ کل");
            sb.AppendLine("───────────┼────────────┼──────────");
            foreach (var order in sellOrders)
            {
                var remaining = order.Quantity - order.FilledQuantity;
                sb.AppendLine($"{order.Price,10:N0} │ {remaining,10:N0} │ {remaining * order.Price,10:N0}");
            }
            if (sellOrders.Count == 0) sb.AppendLine("   سفارشی نیست");

            var spread = sellOrders.Count > 0 && buyOrders.Count > 0
                ? sellOrders.First().Price - buyOrders.First().Price
                : 0;

            sb.AppendLine($"\n📊 Spread: {spread:N0} ({(coin.CurrentPrice > 0 ? spread / coin.CurrentPrice * 100 : 0):F2}%)");

            await _api.SendMessage(chatId, sb.ToString());
        }

        private async Task HandleLeaderboard(long chatId)
        {
            var topUsers = _dbManager.Db.Users.Values
                .Where(u => !u.IsBanned)
                .Select(u => new { User = u, Total = _dailyReward.CalculateTotalValue(u) })
                .OrderByDescending(x => x.Total)
                .Take(10)
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine("🏆 <b>رتبه‌بندی برترین‌ها</b>\n");
            sb.AppendLine("━━━━━━━━━━━━━━━━━━");

            var medals = new[] { "🥇", "🥈", "🥉" };

            for (int i = 0; i < topUsers.Count; i++)
            {
                var entry = medals.ElementAtOrDefault(i) ?? $"#{i + 1}";
                var displayName = !string.IsNullOrEmpty(topUsers[i].User.Username)
                    ? $"@{topUsers[i].User.Username}"
                    : topUsers[i].User.FirstName;

                sb.AppendLine($"{entry} {displayName}");
                sb.AppendLine($"   💰 {topUsers[i].Total:N0} سکه");
            }

            sb.AppendLine("━━━━━━━━━━━━━━━━━━");

            // User's rank
            if (_dbManager.Db.Users.Count > 0)
            {
                var allUsers = _dbManager.Db.Users.Values
                    .Where(u => !u.IsBanned)
                    .Select(u => new { User = u, Total = _dailyReward.CalculateTotalValue(u) })
                    .OrderByDescending(x => x.Total)
                    .ToList();

                var myIndex = allUsers.FindIndex(x => x.User.Id == chatId);
                if (myIndex >= 0)
                {
                    sb.AppendLine($"\n📍 رتبه شما: <b>#{myIndex + 1}</b> از {allUsers.Count}");
                }
            }

            await _api.SendMessage(chatId, sb.ToString());
        }

        private async Task HandleDailyStatus(long chatId, long userId)
        {
            if (!_dbManager.Db.Users.ContainsKey(userId)) return;
            var user = _dbManager.Db.Users[userId];
            var totalValue = _dailyReward.CalculateTotalValue(user);

            var tehranNow = DateTime.UtcNow.AddHours(3.5);
            var nextMidnight = tehranNow.Date.AddDays(1);
            var hoursUntil = (nextMidnight - tehranNow).TotalHours;

            var sb = new StringBuilder();
            sb.AppendLine("🎁 <b>جایزه روزانه</b>\n");
            sb.AppendLine("━━━━━━━━━━━━━━━━━━");
            sb.AppendLine($"💰 ارزش کل دارایی شما: <b>{totalValue:N0}</b>");
            sb.AppendLine($"🎯 حد دریافت جایزه: <b>{DAILY_REWARD_THRESHOLD:N0}</b>\n");

            if (totalValue < DAILY_REWARD_THRESHOLD)
            {
                sb.AppendLine($"✅ شما واجد شرایط دریافت جایزه هستید!");
                sb.AppendLine($"🎁 مبلغ جایزه: <b>{DAILY_REWARD_AMOUNT:N0}</b> سکه\n");

                if (user.LastDaily.HasValue)
                {
                    var lastDailyTehran = user.LastDaily.Value.AddHours(3.5);
                    sb.AppendLine($"⏰ آخرین دریافت: {lastDailyTehran:yyyy/MM/dd HH:mm}");
                }
            }
            else
            {
                sb.AppendLine($"❌ دارایی شما بالاتر از حد مجاز است.");
                sb.AppendLine($"📊 باید {totalValue - DAILY_REWARD_THRESHOLD:N0} کمتر شود.");
            }

            sb.AppendLine($"\n⏳ زمان باقی‌مانده تا جایزه بعدی: <b>{hoursUntil:F1}</b> ساعت");
            sb.AppendLine("━━━━━━━━━━━━━━━━━━");

            await _api.SendMessage(chatId, sb.ToString());
        }

        private async Task HandleHelp(long chatId)
        {
            var sb = new StringBuilder();
            sb.AppendLine("❓ <b>راهنمای بات بازار سهام</b>\n");
            sb.AppendLine("━━━━━━━━━━━━━━━━━━");
            sb.AppendLine("<b>📊 دستورات بازار:</b>");
            sb.AppendLine("  /market - لیست ارزها");
            sb.AppendLine("  /trade SYMBOL - جزئیات ارز");
            sb.AppendLine("  /chart SYMBOL - نمودار قیمت");
            sb.AppendLine("  /orderbook SYMBOL - دفتر سفارش");
            sb.AppendLine("  /history SYMBOL - تاریخچه معاملات");
            sb.AppendLine();
            sb.AppendLine("<b>💼 دستورات حساب:</b>");
            sb.AppendLine("  /portfolio - دارایی‌ها");
            sb.AppendLine("  /balance - موجودی");
            sb.AppendLine("  /orders - سفارش‌های باز");
            sb.AppendLine("  /profile - پروفایل");
            sb.AppendLine();
            sb.AppendLine("<b>🛒 معاملات:</b>");
            sb.AppendLine("  /buy SYMBOL QTY PRICE - خرید");
            sb.AppendLine("  /sell SYMBOL QTY PRICE - فروش");
            sb.AppendLine("  /cancel ORDER_ID - لغو سفارش");
            sb.AppendLine();
            sb.AppendLine("<b>🏆 سایر:</b>");
            sb.AppendLine("  /leaderboard - رتبه‌بندی");
            sb.AppendLine("  /daily - جایزه روزانه");
            sb.AppendLine("  /stats - آمار بازار");
            sb.AppendLine();
            sb.AppendLine("<b>📝 مثال:</b>");
            sb.AppendLine("  /buy BTC 10 500");
            sb.AppendLine("  /sell BTC 5 600");
            sb.AppendLine("━━━━━━━━━━━━━━━━━━");

            await _api.SendMessage(chatId, sb.ToString());
        }

        private async Task HandleStats(long chatId)
        {
            var db = _dbManager.Db;
            var sb = new StringBuilder();
            sb.AppendLine("📊 <b>آمار بازار</b>\n");
            sb.AppendLine("━━━━━━━━━━━━━━━━━━");
            sb.AppendLine($"👥 کاربران: <b>{db.Users.Count}</b>");
            sb.AppendLine($"🪙 ارزها: <b>{db.Coins.Count}</b>");
            sb.AppendLine($"📋 سفارش‌ها: <b>{db.Orders.Count}</b>");
            sb.AppendLine($"💹 معاملات: <b>{db.Trades.Count}</b>\n");

            var totalMarketCap = db.Coins.Values.Sum(c => c.MarketCap);
            var totalVolume = db.Coins.Values.Sum(c => c.Volume24h);
            sb.AppendLine($"💰 مارکت‌کپ کل: <b>{totalMarketCap:N0}</b>");
            sb.AppendLine($"📊 حجم ۲۴h: <b>{totalVolume:N0}</b>");

            if (db.Trades.Count > 0)
            {
                var lastTrade = db.Trades.OrderByDescending(t => t.Timestamp).First();
                sb.AppendLine($"⏰ آخرین معامله: {lastTrade.Timestamp:HH:mm}");
            }

            sb.AppendLine("━━━━━━━━━━━━━━━━━━");
            await _api.SendMessage(chatId, sb.ToString());
        }

        private async Task HandleProfile(long chatId, long userId)
        {
            if (!_dbManager.Db.Users.ContainsKey(userId)) return;
            var user = _dbManager.Db.Users[userId];
            var totalValue = _dailyReward.CalculateTotalValue(user);
            var totalTrades = user.TradeCount;
            var totalVol = user.TotalVolume;

            var rank = _dbManager.Db.Users.Values
                .Where(u => !u.IsBanned)
                .Select(u => _dailyReward.CalculateTotalValue(u))
                .OrderByDescending(v => v)
                .ToList()
                .IndexOf(totalValue) + 1;

            var sb = new StringBuilder();
            sb.AppendLine("👤 <b>پروفایل شما</b>\n");
            sb.AppendLine("━━━━━━━━━━━━━━━━━━");
            sb.AppendLine($"📛 نام: {GetDisplayName(user)}");
            sb.AppendLine($"🆔 آیدی: <code>{userId}</code>");
            sb.AppendLine($"📅 عضویت: {user.RegisteredAt:yyyy/MM/dd}");
            sb.AppendLine($"🏆 رتبه: #{rank}");
            sb.AppendLine($"💰 موجودی: {user.Balance:N0}");
            sb.AppendLine($"📊 ارزش کل: {totalValue:N0}");
            sb.AppendLine($"📋 معاملات: {totalTrades}");
            sb.AppendLine($"💹 حجم کل: {totalVol:N0}");

            if (user.Flagged)
            {
                sb.AppendLine($"\n⚠️ <b>حساب شما مشکوک شناسایی شده!</b>");
            }

            sb.AppendLine("━━━━━━━━━━━━━━━━━━");

            await _api.SendMessage(chatId, sb.ToString());
        }

        // ====================================================================
        // Admin Commands
        // ====================================================================

        private bool IsOwner(long userId) => userId == OWNER_ID;

        private async Task HandleAdmin(long chatId, long userId)
        {
            if (!IsOwner(userId))
            {
                await _api.SendMessage(chatId, "⛔ دسترسی غیرمجاز!");
                return;
            }

            var keyboard = new InlineKeyboard()
                .AddButton("🪙 اضافه ارز", "admin_addcoin")
                .AddButton("💲 تنظیم قیمت", "admin_setprice")
                .NewRow()
                .AddButton("👥 لیست کاربران", "admin_userlist")
                .AddButton("🔍 اطلاعات کاربر", "admin_userinfo")
                .NewRow()
                .AddButton("🚫 بن کاربر", "admin_ban")
                .AddButton("✅ آنبن", "admin_unban")
                .NewRow()
                .AddButton("📢 ارسال همگانی", "admin_broadcast")
                .AddButton("💰 تنظیم موجودی", "admin_setbal")
                .NewRow()
                .AddButton("⛔ تعلیق ارز", "admin_suspend")
                .AddButton("✅ رفع تعلیق", "admin_unsuspend")
                .NewRow()
                .AddButton("📊 آمار جهانی", "admin_globalstats")
                .AddButton("🚩 حساب‌های مشکوک", "admin_flagged")
                .NewRow()
                .AddButton("🎁 توزیع دستی جایزه", "admin_daily")
                .Build();

            await _api.SendMessage(chatId,
                $"⚙️ <b>پنل مدیریت</b>\n\n" +
                $"👥 کاربران: {_dbManager.Db.Users.Count}\n" +
                $"🪙 ارزها: {_dbManager.Db.Coins.Count}\n" +
                $"📋 سفارش‌ها: {_dbManager.Db.Orders.Count}\n" +
                $"💹 معاملات: {_dbManager.Db.Trades.Count}",
                replyMarkup: keyboard);
        }

        private async Task HandleAddCoin(long chatId, long userId)
        {
            if (!IsOwner(userId))
            {
                await _api.SendMessage(chatId, "⛔ دسترسی غیرمجاز!");
                return;
            }

            _sessions[userId] = new UserSession
            {
                State = "addcoin_symbol",
                Data = new()
            };

            await _api.SendMessage(chatId,
                "🪙 <b>اضافه کردن ارز جدید</b>\n\n" +
                "مرحله ۱/۴: <b>نماد ارز</b> را وارد کنید\n" +
                "(مثال: BTC, ETH, GOLD)\n\n" +
                "❌ لغو: /cancel");
        }

        private async Task StartBuyFlow(long chatId, long userId)
        {
            var coins = _dbManager.Db.Coins.Values.Where(c => c.IsActive && !c.IsSuspended).ToList();
            if (coins.Count == 0)
            {
                await _api.SendMessage(chatId, "📊 هیچ ارزی برای معامله وجود ندارد!");
                return;
            }

            _sessions[userId] = new UserSession { State = "buy_symbol" };

            var keyboard = new InlineKeyboard();
            foreach (var coin in coins.Take(8))
            {
                keyboard.AddButton($"{coin.Symbol} ({coin.CurrentPrice:N0})", $"buymenu_{coin.Symbol}");
                keyboard.NewRow();
            }
            keyboard.AddButton("❌ لغو", "cmd_menu");

            await _api.SendMessage(chatId, "🛒 <b>خرید</b>\n\nارز مورد نظر را انتخاب کنید:", replyMarkup: keyboard.Build());
        }

        private async Task StartSellFlow(long chatId, long userId)
        {
            var user = _dbManager.Db.Users[userId];
            var holdings = user.Portfolio.Where(p => p.Value > 0).ToList();

            if (holdings.Count == 0)
            {
                await _api.SendMessage(chatId, "💼 هیچ دارایی برای فروش ندارید!");
                return;
            }

            _sessions[userId] = new UserSession { State = "sell_symbol" };

            var keyboard = new InlineKeyboard();
            foreach (var h in holdings)
            {
                if (_dbManager.Db.Coins.ContainsKey(h.Key))
                {
                    var coin = _dbManager.Db.Coins[h.Key];
                    keyboard.AddButton($"{h.Key} ({h.Value:N0}) @{coin.CurrentPrice:N0}", $"sellmenu_{h.Key}");
                    keyboard.NewRow();
                }
            }
            keyboard.AddButton("❌ لغو", "cmd_menu");

            await _api.SendMessage(chatId, "💸 <b>فروش</b>\n\nارز مورد نظر را انتخاب کنید:", replyMarkup: keyboard.Build());
        }

        private async Task HandleSetPrice(long chatId, long userId, string symbol, string priceStr)
        {
            if (!IsOwner(userId)) return;

            if (!decimal.TryParse(priceStr, out var price) || price <= 0)
            {
                await _api.SendMessage(chatId, "❌ قیمت نامعتبر!");
                return;
            }

            if (!_dbManager.Db.Coins.ContainsKey(symbol))
            {
                await _api.SendMessage(chatId, $"❌ ارز {symbol} یافت نشد!");
                return;
            }

            _dbManager.Db.Coins[symbol].CurrentPrice = price;
            _dbManager.Db.Coins[symbol].PriceHistory.Add(new PricePoint
            {
                Timestamp = DateTime.UtcNow,
                Price = price
            });
            _dbManager.Db.Coins[symbol].MarketCap = price * _dbManager.Db.Coins[symbol].CirculatingSupply;
            _dbManager.Save();

            await _api.SendMessage(chatId, $"✅ قیمت {symbol} به <b>{price:N0}</b> تنظیم شد.");
        }

        private async Task HandleSetUserBalance(long chatId, long userId, string targetIdStr, string balanceStr)
        {
            if (!IsOwner(userId)) return;

            if (!long.TryParse(targetIdStr, out var targetId))
            {
                await _api.SendMessage(chatId, "❌ آیدی نامعتبر!");
                return;
            }

            if (!decimal.TryParse(balanceStr, out var balance) || balance < 0)
            {
                await _api.SendMessage(chatId, "❌ مبلغ نامعتبر!");
                return;
            }

            if (!_dbManager.Db.Users.ContainsKey(targetId))
            {
                await _api.SendMessage(chatId, $"❌ کاربر یافت نشد!");
                return;
            }

            _dbManager.Db.Users[targetId].Balance = balance;
            _dbManager.Save();

            await _api.SendMessage(chatId, $"✅ موجودی کاربر <code>{targetId}</code> به <b>{balance:N0}</b> تنظیم شد.");
        }

        private async Task HandleBan(long chatId, long userId, string targetIdStr)
        {
            if (!IsOwner(userId)) return;

            if (!long.TryParse(targetIdStr, out var targetId))
            {
                await _api.SendMessage(chatId, "❌ آیدی نامعتبر!");
                return;
            }

            if (!_dbManager.Db.Users.ContainsKey(targetId))
            {
                await _api.SendMessage(chatId, "❌ کاربر یافت نشد!");
                return;
            }

            _dbManager.Db.Users[targetId].IsBanned = true;
            if (!_dbManager.Db.BannedUsers.Contains(targetId))
                _dbManager.Db.BannedUsers.Add(targetId);
            _dbManager.Save();

            await _api.SendMessage(chatId, $"🚫 کاربر <code>{targetId}</code> بن شد.");

            try { await _api.SendMessage(targetId, "⛔ شما از بات بن شده‌اید."); } catch { }
        }

        private async Task HandleUnban(long chatId, long userId, string targetIdStr)
        {
            if (!IsOwner(userId)) return;

            if (!long.TryParse(targetIdStr, out var targetId))
            {
                await _api.SendMessage(chatId, "❌ آیدی نامعتبر!");
                return;
            }

            if (_dbManager.Db.Users.ContainsKey(targetId))
            {
                _dbManager.Db.Users[targetId].IsBanned = false;
            }
            _dbManager.Db.BannedUsers.Remove(targetId);
            _dbManager.Save();

            await _api.SendMessage(chatId, $"✅ کاربر <code>{targetId}</code> آنبن شد.");
        }

        private async Task HandleBroadcast(long chatId, long userId, string message)
        {
            if (!IsOwner(userId)) return;

            await _api.SendMessage(chatId, $"📢 در حال ارسال پیام به {_dbManager.Db.Users.Count} کاربر...");

            int sent = 0, failed = 0;
            foreach (var user in _dbManager.Db.Users.Values)
            {
                try
                {
                    await _api.SendMessage(user.Id, $"📢 <b>پیام ادمین:</b>\n\n{message}");
                    sent++;
                    await Task.Delay(50); // Rate limiting
                }
                catch
                {
                    failed++;
                }
            }

            _dbManager.Db.BroadcastHistory.Add(new BroadcastRecord
            {
                Message = message[..Math.Min(message.Length, 100)],
                UserCount = sent
            });
            _dbManager.Save();

            await _api.SendMessage(chatId, $"📢 ارسال شد!\n✅ موفق: {sent}\n❌ ناموفق: {failed}");
        }

        private async Task StartBroadcastFlow(long chatId, long userId)
        {
            if (!IsOwner(userId)) return;

            _sessions[userId] = new UserSession { State = "broadcast" };
            await _api.SendMessage(chatId, "📢 پیام خود را وارد کنید:");
        }

        private async Task HandleUserList(long chatId, long userId)
        {
            if (!IsOwner(userId)) return;

            var users = _dbManager.Db.Users.Values
                .OrderByDescending(u => u.LastActive)
                .Take(20)
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine("👥 <b>لیست کاربران</b> (آخرین ۲۰)\n");
            sb.AppendLine("━━━━━━━━━━━━━━━━━━");

            foreach (var user in users)
            {
                var status = user.IsBanned ? "🚫" : user.Flagged ? "🚩" : "✅";
                var totalValue = _dailyReward.CalculateTotalValue(user);
                sb.AppendLine($"{status} {GetDisplayName(user)}");
                sb.AppendLine($"   ID: <code>{user.Id}</code>");
                sb.AppendLine($"   💰 {user.Balance:N0} | 📊 {totalValue:N0}");
                sb.AppendLine($"   🕐 {user.LastActive:MM/dd HH:mm}");
                sb.AppendLine();
            }

            var keyboard = new InlineKeyboard()
                .AddButton("🔄 بروزرسانی", "admin_userlist")
                .NewRow()
                .AddButton("🔙 پنل", "admin_panel")
                .Build();

            await _api.SendMessage(chatId, sb.ToString(), replyMarkup: keyboard);
        }

        private async Task HandleUserInfo(long chatId, long userId, string targetIdStr)
        {
            if (!IsOwner(userId)) return;

            if (!long.TryParse(targetIdStr, out var targetId))
            {
                await _api.SendMessage(chatId, "❌ آیدی نامعتبر!");
                return;
            }

            if (!_dbManager.Db.Users.ContainsKey(targetId))
            {
                await _api.SendMessage(chatId, "❌ کاربر یافت نشد!");
                return;
            }

            var user = _dbManager.Db.Users[targetId];
            var totalValue = _dailyReward.CalculateTotalValue(user);
            var altAccounts = _antiCheat.GetAllAltAccounts(targetId);

            var sb = new StringBuilder();
            sb.AppendLine($"🔍 <b>اطلاعات کاربر</b>\n");
            sb.AppendLine("━━━━━━━━━━━━━━━━━━");
            sb.AppendLine($"📛 نام: {GetDisplayName(user)}");
            sb.AppendLine($"🆔 ID: <code>{targetId}</code>");
            sb.AppendLine($"👤 یوزرنیم: @{user.Username}");
            sb.AppendLine($"📅 عضویت: {user.RegisteredAt:yyyy/MM/dd HH:mm}");
            sb.AppendLine($"🕐 آخرین فعالیت: {user.LastActive:yyyy/MM/dd HH:mm}");
            sb.AppendLine();
            sb.AppendLine($"💰 موجودی: <b>{user.Balance:N0}</b>");
            sb.AppendLine($"📊 ارزش کل: <b>{totalValue:N0}</b>");
            sb.AppendLine($"📋 معاملات: {user.TradeCount}");
            sb.AppendLine($"💹 حجم: {user.TotalVolume:N0}");
            sb.AppendLine();
            sb.AppendLine($"🔖 فینگرپرنت: {(string.IsNullOrEmpty(user.Fingerprint) ? "—" : $"<code>{user.Fingerprint[..Math.Min(user.Fingerprint.Length, 16)]}</code>")}");
            sb.AppendLine($"🚩 مشکوک: {(user.Flagged ? "بله" : "خیر")}");
            sb.AppendLine($"🚫 بن: {(user.IsBanned ? "بله" : "خیر")}");

            if (altAccounts.Count > 1)
            {
                sb.AppendLine($"\n⚠️ <b>اکانت‌های مرتبط:</b>");
                foreach (var altId in altAccounts)
                {
                    if (altId != targetId && _dbManager.Db.Users.ContainsKey(altId))
                    {
                        var altUser = _dbManager.Db.Users[altId];
                        sb.AppendLine($"  ▫️ <code>{altId}</code> - {GetDisplayName(altUser)}");
                    }
                }
            }

            if (user.Portfolio.Count > 0)
            {
                sb.AppendLine($"\n<b>💼 دارایی‌ها:</b>");
                foreach (var kvp in user.Portfolio.Where(p => p.Value > 0))
                {
                    sb.AppendLine($"  ▫️ {kvp.Key}: {kvp.Value:N0}");
                }
            }

            sb.AppendLine("━━━━━━━━━━━━━━━━━━");

            var keyboard = new InlineKeyboard()
                .AddButton("💰 تنظیم موجودی", $"admin_setbal_{targetId}")
                .AddButton(user.IsBanned ? "✅ آنبن" : "🚫 بن", $"admin_toggleban_{targetId}")
                .NewRow()
                .AddButton("🔄 ریست موجودی", $"admin_resetbal_{targetId}")
                .AddButton("🔙 لیست", "admin_userlist")
                .Build();

            await _api.SendMessage(chatId, sb.ToString(), replyMarkup: keyboard);
        }

        private async Task HandleSuspendCoin(long chatId, long userId, string symbol)
        {
            if (!IsOwner(userId)) return;

            if (!_dbManager.Db.Coins.ContainsKey(symbol))
            {
                await _api.SendMessage(chatId, $"❌ ارز {symbol} یافت نشد!");
                return;
            }

            _dbManager.Db.Coins[symbol].IsSuspended = true;
            _dbManager.Save();

            await _api.SendMessage(chatId, $"⛔ ارز {symbol} معلق شد (غیرقابل معامله).");
        }

        private async Task HandleUnsuspendCoin(long chatId, long userId, string symbol)
        {
            if (!IsOwner(userId)) return;

            if (!_dbManager.Db.Coins.ContainsKey(symbol))
            {
                await _api.SendMessage(chatId, $"❌ ارز {symbol} یافت نشد!");
                return;
            }

            _dbManager.Db.Coins[symbol].IsSuspended = false;
            _dbManager.Save();

            await _api.SendMessage(chatId, $"✅ تعلیق ارز {symbol} برداشته شد.");
        }

        private async Task HandleResetBalance(long chatId, long userId, string targetIdStr)
        {
            if (!IsOwner(userId)) return;

            if (!long.TryParse(targetIdStr, out var targetId))
            {
                await _api.SendMessage(chatId, "❌ آیدی نامعتبر!");
                return;
            }

            if (!_dbManager.Db.Users.ContainsKey(targetId))
            {
                await _api.SendMessage(chatId, "❌ کاربر یافت نشد!");
                return;
            }

            // Cancel all open orders
            var openOrders = _dbManager.Db.Orders
                .Where(o => o.UserId == targetId && (o.Status == "open" || o.Status == "partial"))
                .ToList();

            foreach (var order in openOrders)
            {
                _matchingEngine.CancelOrder(targetId, order.Id);
            }

            _dbManager.Db.Users[targetId].Balance = INITIAL_BALANCE;
            _dbManager.Db.Users[targetId].Portfolio.Clear();
            _dbManager.Save();

            await _api.SendMessage(chatId, $"✅ موجودی کاربر <code>{targetId}</code> به {INITIAL_BALANCE:N0} ریست شد.\n📋 {openOrders.Count} سفارش لغو شد.");
        }

        private async Task HandleForceOrder(long chatId, long userId, string type, string symbol, string qtyStr, string priceStr)
        {
            if (!IsOwner(userId)) return;

            if (!_dbManager.Db.Coins.ContainsKey(symbol))
            {
                await _api.SendMessage(chatId, $"❌ ارز {symbol} یافت نشد!");
                return;
            }

            if (!decimal.TryParse(qtyStr, out var qty) || !decimal.TryParse(priceStr, out var price))
            {
                await _api.SendMessage(chatId, "❌ مقادیر نامعتبر!");
                return;
            }

            // Create a virtual order from admin
            var order = new Order
            {
                UserId = userId,
                Type = type,
                Symbol = symbol,
                Quantity = qty,
                Price = price
            };

            // Ensure admin has enough for the operation
            if (type == "buy")
            {
                _dbManager.Db.Users[userId].Balance += qty * price; // Add enough balance
            }
            else
            {
                if (!_dbManager.Db.Users[userId].Portfolio.ContainsKey(symbol))
                    _dbManager.Db.Users[userId].Portfolio[symbol] = 0;
                _dbManager.Db.Users[userId].Portfolio[symbol] += qty; // Add enough coins
            }

            var trades = _matchingEngine.PlaceOrder(order);
            _dbManager.Save();

            await _api.SendMessage(chatId,
                $"⚡ سفارش اجباری {type} انجام شد!\n" +
                $"🪙 {symbol}: {qty:N0} @ {price:N0}\n" +
                $"💹 معاملات: {trades.Count}");
        }

        private async Task HandleManualDaily(long chatId, long userId)
        {
            if (!IsOwner(userId)) return;

            int count = 0;
            foreach (var user in _dbManager.Db.Users.Values)
            {
                if (user.IsBanned || user.IsSuspended) continue;
                var totalValue = _dailyReward.CalculateTotalValue(user);
                if (totalValue < DAILY_REWARD_THRESHOLD)
                {
                    user.Balance += DAILY_REWARD_AMOUNT;
                    user.LastDaily = DateTime.UtcNow;
                    count++;
                }
            }

            _dbManager.Save();
            await _api.SendMessage(chatId, $"🎁 جایزه به {count} کاربر توزیع شد.");
        }

        private async Task HandleGlobalStats(long chatId, long userId)
        {
            if (!IsOwner(userId)) return;

            var db = _dbManager.Db;
            var totalUsers = db.Users.Count;
            var activeUsers = db.Users.Values.Count(u => u.LastActive > DateTime.UtcNow.AddDays(-1));
            var bannedUsers = db.BannedUsers.Count;
            var flaggedUsers = db.Users.Values.Count(u => u.Flagged);

            var totalMarketCap = db.Coins.Values.Sum(c => c.MarketCap);
            var totalVolume24h = db.Coins.Values.Sum(c => c.Volume24h);
            var totalTrades = db.Trades.Count;
            var totalOrders = db.Orders.Count(o => o.Status == "open" || o.Status == "partial");

            var totalBalance = db.Users.Values.Sum(u => u.Balance);
            var avgBalance = totalUsers > 0 ? totalBalance / totalUsers : 0;

            var sb = new StringBuilder();
            sb.AppendLine("📊 <b>آمار جهانی</b>\n");
            sb.AppendLine("━━━━━━━━━━━━━━━━━━");
            sb.AppendLine("<b>👥 کاربران:</b>");
            sb.AppendLine($"  کل: {totalUsers}");
            sb.AppendLine($"  فعال (۲۴h): {activeUsers}");
            sb.AppendLine($"  بن: {bannedUsers}");
            sb.AppendLine($"  مشکوک: {flaggedUsers}");
            sb.AppendLine();
            sb.AppendLine("<b>💰 مالی:</b>");
            sb.AppendLine($"  کل موجودی: {totalBalance:N0}");
            sb.AppendLine($"  میانگین: {avgBalance:N0}");
            sb.AppendLine($"  مارکت‌کپ: {totalMarketCap:N0}");
            sb.AppendLine($"  حجم ۲۴h: {totalVolume24h:N0}");
            sb.AppendLine();
            sb.AppendLine("<b>📋 معاملات:</b>");
            sb.AppendLine($"  کل معاملات: {totalTrades}");
            sb.AppendLine($"  سفارش باز: {totalOrders}");
            sb.AppendLine();
            sb.AppendLine("<b>🪙 ارزها:</b>");
            foreach (var coin in db.Coins.Values)
            {
                sb.AppendLine($"  {coin.Symbol}: {coin.CurrentPrice:N0} ({coin.HolderCount} holder)");
            }
            sb.AppendLine("━━━━━━━━━━━━━━━━━━");

            await _api.SendMessage(chatId, sb.ToString());
        }

        private async Task HandleFlaggedUsers(long chatId, long userId)
        {
            if (!IsOwner(userId)) return;

            var flagged = _dbManager.Db.Users.Values
                .Where(u => u.Flagged)
                .ToList();

            if (flagged.Count == 0)
            {
                await _api.SendMessage(chatId, "✅ هیچ حساب مشکوکی شناسایی نشده.");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("🚩 <b>حساب‌های مشکوک</b>\n");
            sb.AppendLine("━━━━━━━━━━━━━━━━━━");

            foreach (var user in flagged)
            {
                var altAccounts = _antiCheat.GetAllAltAccounts(user.Id);
                sb.AppendLine($"⚠️ {GetDisplayName(user)} (<code>{user.Id}</code>)");
                sb.AppendLine($"   💰 {user.Balance:N0} | 📋 {user.TradeCount} معامله");
                if (altAccounts.Count > 1)
                {
                    sb.AppendLine($"   🔗 اکانت‌های مرتبط: {string.Join(", ", altAccounts)}");
                }
                sb.AppendLine();
            }

            sb.AppendLine("━━━━━━━━━━━━━━━━━━");
            await _api.SendMessage(chatId, sb.ToString());
        }

        // ====================================================================
        // Callback Handler
        // ====================================================================

        private async Task ProcessCallback(JsonElement callback)
        {
            var callbackId = callback.GetProperty("id").GetString() ?? "";
            var userId = callback.GetProperty("from").GetProperty("id").GetInt64();
            var data = callback.TryGetProperty("data", out var d) ? d.GetString() ?? "" : "";
            var chatId = userId;

            // Answer callback to remove loading
            await _api.AnswerCallback(callbackId);

            if (string.IsNullOrEmpty(data)) return;

            var parts = data.Split('_');
            var prefix = parts[0];

            switch (data)
            {
                case "cmd_market":
                    await HandleMarket(chatId);
                    break;
                case "cmd_portfolio":
                    await HandlePortfolio(chatId, userId);
                    break;
                case "cmd_menu":
                    await HandleMenu(chatId, userId);
                    break;
                case "cmd_orders":
                    await HandleOpenOrders(chatId, userId);
                    break;
                case "cmd_leaderboard":
                    await HandleLeaderboard(chatId);
                    break;
                case "cmd_help":
                    await HandleHelp(chatId);
                    break;
                case "cmd_stats":
                    await HandleStats(chatId);
                    break;
                case "cmd_profile":
                    await HandleProfile(chatId, userId);
                    break;
                case "cmd_daily":
                    await HandleDailyStatus(chatId, userId);
                    break;
                case "cmd_chart_menu":
                    var chartCoins = _dbManager.Db.Coins.Values.Where(c => c.IsActive).ToList();
                    var chartKb = new InlineKeyboard();
                    foreach (var c in chartCoins.Take(8))
                    {
                        chartKb.AddButton($"📈 {c.Symbol}", $"cmd_chart_{c.Symbol}");
                        chartKb.NewRow();
                    }
                    chartKb.AddButton("🔙 منو", "cmd_menu");
                    await _api.SendMessage(chatId, "📈 ارزی را برای نمودار انتخاب کنید:", replyMarkup: chartKb.Build());
                    break;
                case "cmd_buy_menu":
                    await StartBuyFlow(chatId, userId);
                    break;
                case "cmd_sell_menu":
                    await StartSellFlow(chatId, userId);
                    break;
                case "cmd_refresh_main":
                    await HandleStart(chatId, userId);
                    break;
                case var s when s.StartsWith("cmd_trade_"):
                    var symbol1 = data["cmd_trade_".Length..];
                    await HandleTrade(chatId, userId, symbol1);
                    break;
                case var s when s.StartsWith("cmd_chart_"):
                    var symbol2 = data["cmd_chart_".Length..];
                    await HandleChart(chatId, symbol2);
                    break;
                case var s when s.StartsWith("cmd_orderbook_"):
                    var symbol3 = data["cmd_orderbook_".Length..];
                    await HandleOrderBook(chatId, symbol3);
                    break;
                case var s when s.StartsWith("cmd_buy_"):
                    var symbol4 = data["cmd_buy_".Length..];
                    _sessions[userId] = new UserSession { State = "buy_quantity", Data = new() { ["symbol"] = symbol4 } };
                    await _api.SendMessage(chatId, $"🛒 <b>خرید {symbol4}</b>\n\n💎 قیمت: {_dbManager.Db.Coins[symbol4].CurrentPrice:N0}\n\n📝 تعداد را وارد کنید:");
                    break;
                case var s when s.StartsWith("cmd_sell_"):
                    var symbol5 = data["cmd_sell_".Length..];
                    _sessions[userId] = new UserSession { State = "sell_quantity", Data = new() { ["symbol"] = symbol5 } };
                    var holding5 = _dbManager.Db.Users.ContainsKey(userId) && _dbManager.Db.Users[userId].Portfolio.ContainsKey(symbol5)
                        ? _dbManager.Db.Users[userId].Portfolio[symbol5] : 0;
                    await _api.SendMessage(chatId, $"💸 <b>فروش {symbol5}</b>\n\n💎 قیمت: {_dbManager.Db.Coins[symbol5].CurrentPrice:N0}\n💼 موجودی: {holding5:N0}\n\n📝 تعداد را وارد کنید:");
                    break;
                case var s when s.StartsWith("cmd_cancel_"):
                    var orderId = data["cmd_cancel_".Length..];
                    await HandleCancelOrder(chatId, userId, orderId);
                    break;
                case var s when s.StartsWith("buymenu_"):
                    var bSym = data["buymenu_".Length..];
                    _sessions[userId] = new UserSession { State = "buy_quantity", Data = new() { ["symbol"] = bSym } };
                    await _api.SendMessage(chatId, $"🛒 <b>خرید {bSym}</b>\n\n💎 قیمت: {_dbManager.Db.Coins[bSym].CurrentPrice:N0}\n💰 موجودی: {_dbManager.Db.Users[userId].Balance:N0}\n\n📝 تعداد را وارد کنید:");
                    break;
                case var s when s.StartsWith("sellmenu_"):
                    var sSym = data["sellmenu_".Length..];
                    _sessions[userId] = new UserSession { State = "sell_quantity", Data = new() { ["symbol"] = sSym } };
                    var holding = _dbManager.Db.Users.ContainsKey(userId) && _dbManager.Db.Users[userId].Portfolio.ContainsKey(sSym)
                        ? _dbManager.Db.Users[userId].Portfolio[sSym] : 0;
                    await _api.SendMessage(chatId, $"💸 <b>فروش {sSym}</b>\n\n💎 قیمت: {_dbManager.Db.Coins[sSym].CurrentPrice:N0}\n💼 موجودی: {holding:N0}\n\n📝 تعداد را وارد کنید:");
                    break;
                // Admin callbacks
                case "admin_panel":
                    await HandleAdmin(chatId, userId);
                    break;
                case "admin_addcoin":
                    await HandleAddCoin(chatId, userId);
                    break;
                case "admin_userlist":
                    await HandleUserList(chatId, userId);
                    break;
                case "admin_userinfo":
                    _sessions[userId] = new UserSession { State = "admin_userinfo" };
                    await _api.SendMessage(chatId, "🔍 آیدی عددی کاربر را وارد کنید:");
                    break;
                case "admin_setprice":
                    _sessions[userId] = new UserSession { State = "admin_setprice_symbol" };
                    await _api.SendMessage(chatId, "💲 نماد ارز را وارد کنید:");
                    break;
                case "admin_broadcast":
                    await StartBroadcastFlow(chatId, userId);
                    break;
                case "admin_setbal":
                    _sessions[userId] = new UserSession { State = "admin_setbal_id" };
                    await _api.SendMessage(chatId, "🆔 آیدی عددی کاربر را وارد کنید:");
                    break;
                case "admin_ban":
                    _sessions[userId] = new UserSession { State = "admin_ban_id" };
                    await _api.SendMessage(chatId, "🚫 آیدی عددی کاربر را وارد کنید:");
                    break;
                case "admin_unban":
                    _sessions[userId] = new UserSession { State = "admin_unban_id" };
                    await _api.SendMessage(chatId, "✅ آیدی عددی کاربر را وارد کنید:");
                    break;
                case "admin_suspend":
                    _sessions[userId] = new UserSession { State = "admin_suspend_sym" };
                    await _api.SendMessage(chatId, "⛔ نماد ارز را وارد کنید:");
                    break;
                case "admin_unsuspend":
                    _sessions[userId] = new UserSession { State = "admin_unsuspend_sym" };
                    await _api.SendMessage(chatId, "✅ نماد ارز را وارد کنید:");
                    break;
                case "admin_globalstats":
                    await HandleGlobalStats(chatId, userId);
                    break;
                case "admin_flagged":
                    await HandleFlaggedUsers(chatId, userId);
                    break;
                case "admin_daily":
                    await HandleManualDaily(chatId, userId);
                    break;
                case var s when s.StartsWith("admin_setbal_"):
                    var setBalId = data["admin_setbal_".Length..];
                    _sessions[userId] = new UserSession { State = "admin_setbal_amount", Data = new() { ["targetId"] = setBalId } };
                    await _api.SendMessage(chatId, $"💰 مبلغ جدید موجودی برای <code>{setBalId}</code>:");
                    break;
                case var s when s.StartsWith("admin_toggleban_"):
                    var toggleId = data["admin_toggleban_".Length..];
                    if (long.TryParse(toggleId, out var tId) && _dbManager.Db.Users.ContainsKey(tId))
                    {
                        if (_dbManager.Db.Users[tId].IsBanned)
                        {
                            _dbManager.Db.Users[tId].IsBanned = false;
                            _dbManager.Db.BannedUsers.Remove(tId);
                            await _api.SendMessage(chatId, $"✅ کاربر <code>{tId}</code> آنبن شد.");
                        }
                        else
                        {
                            _dbManager.Db.Users[tId].IsBanned = true;
                            if (!_dbManager.Db.BannedUsers.Contains(tId))
                                _dbManager.Db.BannedUsers.Add(tId);
                            await _api.SendMessage(chatId, $"🚫 کاربر <code>{tId}</code> بن شد.");
                        }
                        _dbManager.Save();
                    }
                    break;
                case var s when s.StartsWith("admin_resetbal_"):
                    var resetId = data["admin_resetbal_".Length..];
                    await HandleResetBalance(chatId, userId, resetId);
                    break;
                default:
                    await _api.SendMessage(chatId, "❓ دستور ناشناخته");
                    break;
            }
        }

        // ====================================================================
        // Session Handler (Multi-step flows)
        // ====================================================================

        private async Task HandleSessionInput(long userId, long chatId, string text, UserSession session)
        {
            // Check for cancel
            if (text == "/cancel")
            {
                session.State = "idle";
                session.Data.Clear();
                await _api.SendMessage(chatId, "❌ عملیات لغو شد.");
                return;
            }

            switch (session.State)
            {
                // Add Coin Flow
                case "addcoin_symbol":
                    session.Data["symbol"] = text.ToUpper().Trim();
                    if (_dbManager.Db.Coins.ContainsKey(session.Data["symbol"]))
                    {
                        await _api.SendMessage(chatId, "❌ این نماد قبلاً ثبت شده!");
                        session.State = "idle";
                        return;
                    }
                    session.State = "addcoin_name";
                    await _api.SendMessage(chatId,
                        $"مرحله ۲/۴: <b>نام کامل ارز</b> را وارد کنید\n" +
                        $"(نماد: {session.Data["symbol"]})");
                    break;

                case "addcoin_name":
                    session.Data["name"] = text.Trim();
                    session.State = "addcoin_supply";
                    await _api.SendMessage(chatId,
                        $"مرحله ۳/۴: <b>مقدار کل عرضه</b> را وارد کنید\n" +
                        $"({session.Data["symbol"]} - {session.Data["name"]})\n\n" +
                        $"مثال: 1000000");
                    break;

                case "addcoin_supply":
                    if (!decimal.TryParse(text, out var supply) || supply <= 0)
                    {
                        await _api.SendMessage(chatId, "❌ مقدار نامعتبر! عدد مثبت وارد کنید:");
                        return;
                    }
                    session.Data["supply"] = text;
                    session.State = "addcoin_price";
                    await _api.SendMessage(chatId,
                        $"مرحله ۴/۴: <b>ارزش پایه (قیمت اولیه)</b> را وارد کنید\n" +
                        $"({session.Data["symbol"]} - {session.Data["name"]})\n\n" +
                        $"مثال: 100");
                    break;

                case "addcoin_price":
                    if (!decimal.TryParse(text, out var basePrice) || basePrice <= 0)
                    {
                        await _api.SendMessage(chatId, "❌ قیمت نامعتبر! عدد مثبت وارد کنید:");
                        return;
                    }

                    var totalSupply = decimal.Parse(session.Data["supply"]);
                    var coin = new Coin
                    {
                        Symbol = session.Data["symbol"],
                        Name = session.Data["name"],
                        TotalSupply = totalSupply,
                        BaseValue = basePrice,
                        CurrentPrice = basePrice,
                        CirculatingSupply = totalSupply,
                        MarketCap = basePrice * totalSupply,
                        IsActive = true,
                        High24h = basePrice,
                        Low24h = basePrice,
                        PriceHistory = new List<PricePoint>
                        {
                            new PricePoint { Timestamp = DateTime.UtcNow, Price = basePrice }
                        }
                    };

                    _dbManager.Db.Coins[coin.Symbol] = coin;
                    _dbManager.Save();

                    session.State = "idle";
                    session.Data.Clear();

                    await _api.SendMessage(chatId,
                        $"✅ <b>ارز جدید اضافه شد!</b>\n\n" +
                        $"🪙 نماد: <b>{coin.Symbol}</b>\n" +
                        $"📛 نام: <b>{coin.Name}</b>\n" +
                        $"💰 ارزش پایه: <b>{coin.BaseValue:N0}</b>\n" +
                        $"🪙 عرضه: <b>{coin.TotalSupply:N0}</b>\n" +
                        $"📊 مارکت‌کپ: <b>{coin.MarketCap:N0}</b>");
                    break;

                // Buy Flow
                case "buy_quantity":
                    if (!decimal.TryParse(text, out var buyQty) || buyQty <= 0)
                    {
                        await _api.SendMessage(chatId, "❌ تعداد نامعتبر! عدد مثبت وارد کنید:");
                        return;
                    }
                    session.Data["quantity"] = text;
                    session.State = "buy_price";
                    var buyCoin = _dbManager.Db.Coins[session.Data["symbol"]];
                    await _api.SendMessage(chatId,
                        $"💎 قیمت پیشنهادی هر واحد:\n" +
                        $"💰 قیمت فعلی: <b>{buyCoin.CurrentPrice:N0}</b>\n\n" +
                        $"📝 قیمت خرید خود را وارد کنید:");
                    break;

                case "buy_price":
                    if (!decimal.TryParse(text, out var buyPrice) || buyPrice <= 0)
                    {
                        await _api.SendMessage(chatId, "❌ قیمت نامعتبر!");
                        return;
                    }
                    var buySymbol = session.Data["symbol"];
                    var buyQ = decimal.Parse(session.Data["quantity"]);
                    session.State = "idle";
                    await HandleBuy(chatId, userId, buySymbol, buyQ.ToString(), buyPrice.ToString());
                    break;

                // Sell Flow
                case "sell_quantity":
                    if (!decimal.TryParse(text, out var sellQty) || sellQty <= 0)
                    {
                        await _api.SendMessage(chatId, "❌ تعداد نامعتبر!");
                        return;
                    }
                    var sellSym = session.Data["symbol"];
                    var holdCheck = _dbManager.Db.Users.ContainsKey(userId) && _dbManager.Db.Users[userId].Portfolio.ContainsKey(sellSym)
                        ? _dbManager.Db.Users[userId].Portfolio[sellSym] : 0;
                    if (sellQty > holdCheck)
                    {
                        await _api.SendMessage(chatId, $"❌ موجودی کافی نیست! شما {holdCheck:N0} {sellSym} دارید.");
                        return;
                    }
                    session.Data["quantity"] = text;
                    session.State = "sell_price";
                    var sellCoin = _dbManager.Db.Coins[sellSym];
                    await _api.SendMessage(chatId,
                        $"💎 قیمت پیشنهادی هر واحد:\n" +
                        $"💰 قیمت فعلی: <b>{sellCoin.CurrentPrice:N0}</b>\n\n" +
                        $"📝 قیمت فروش خود را وارد کنید:");
                    break;

                case "sell_price":
                    if (!decimal.TryParse(text, out var sellPrice) || sellPrice <= 0)
                    {
                        await _api.SendMessage(chatId, "❌ قیمت نامعتبر!");
                        return;
                    }
                    var sellSymbol2 = session.Data["symbol"];
                    var sellQ = decimal.Parse(session.Data["quantity"]);
                    session.State = "idle";
                    await HandleSell(chatId, userId, sellSymbol2, sellQ.ToString(), sellPrice.ToString());
                    break;

                // Admin flows
                case "broadcast":
                    session.State = "idle";
                    await HandleBroadcast(chatId, userId, text);
                    break;

                case "admin_userinfo":
                    session.State = "idle";
                    await HandleUserInfo(chatId, userId, text);
                    break;

                case "admin_setprice_symbol":
                    session.Data["symbol"] = text.ToUpper().Trim();
                    session.State = "admin_setprice_value";
                    await _api.SendMessage(chatId, $"💲 قیمت جدید {session.Data["symbol"]}:");
                    break;

                case "admin_setprice_value":
                    session.State = "idle";
                    await HandleSetPrice(chatId, userId, session.Data["symbol"], text);
                    break;

                case "admin_setbal_id":
                    session.Data["targetId"] = text;
                    session.State = "admin_setbal_amount";
                    await _api.SendMessage(chatId, $"💰 مبلغ جدید موجودی برای <code>{text}</code>:");
                    break;

                case "admin_setbal_amount":
                    var targetId = session.Data.ContainsKey("targetId") ? session.Data["targetId"] : "";
                    session.State = "idle";
                    await HandleSetUserBalance(chatId, userId, targetId, text);
                    break;

                case "admin_ban_id":
                    session.State = "idle";
                    await HandleBan(chatId, userId, text);
                    break;

                case "admin_unban_id":
                    session.State = "idle";
                    await HandleUnban(chatId, userId, text);
                    break;

                case "admin_suspend_sym":
                    session.State = "idle";
                    await HandleSuspendCoin(chatId, userId, text.ToUpper());
                    break;

                case "admin_unsuspend_sym":
                    session.State = "idle";
                    await HandleUnsuspendCoin(chatId, userId, text.ToUpper());
                    break;

                default:
                    session.State = "idle";
                    session.Data.Clear();
                    break;
            }

            session.LastActivity = DateTime.UtcNow;
        }

        // ====================================================================
        // Helpers
        // ====================================================================

        private string GetDisplayName(User user)
        {
            if (!string.IsNullOrEmpty(user.Username))
                return $"@{user.Username}";
            if (!string.IsNullOrEmpty(user.FirstName))
                return user.FirstName;
            return $"User_{user.Id}";
        }
    }

    // ========================================================================
    // Program Entry Point
    // ========================================================================

    public class Program
    {
        public static async Task Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine("Starting SahamBot...");

            var bot = new SahamBotController();
            await bot.Run();
        }
    }
}
