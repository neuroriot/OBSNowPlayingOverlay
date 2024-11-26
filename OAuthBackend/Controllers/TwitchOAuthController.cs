using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OAuthBackend.Model.Twitch;
using OAuthBackend.Services;
using OAuthBackend.Services.Auth;
using StackExchange.Redis;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace OAuthBackend.Controllers
{
    // https://github.com/swiftyspiffy/Twitch-Auth-Example
    [Route("[action]")]
    [ApiController]
    public class TwitchOAuthController : Controller
    {
        private readonly ILogger<TwitchOAuthController> _logger;
        private readonly IConfiguration _configuration;
        private readonly RedisService _redisService;
        private readonly TokenService _tokenService;
        private readonly TwitchLib.Api.TwitchAPI _twitchAPI;

        public TwitchOAuthController(ILogger<TwitchOAuthController> logger,
            IConfiguration configuration,
            RedisService redisService,
            TokenService tokenService)
        {
            _logger = logger;
            _configuration = configuration;
            _redisService = redisService;
            _tokenService = tokenService;
            _twitchAPI = new TwitchLib.Api.TwitchAPI()
            {
                Settings =
                {
                     ClientId = _configuration["Twitch:ClientId"],
                     Secret = _configuration["Twitch:ClientSecret"],
                }
            };
        }

        [EnableCors("allowGET")]
        [HttpGet]
        public async Task<IActionResult> GetTwitchOAuthUrl(string state)
        {
            if (string.IsNullOrEmpty(state))
                return new APIResult(ResultStatusCode.BadRequest, "state 不可空白").ToContextResult();

            try
            {
                if (await _redisService.RedisDb.KeyExistsAsync($"nowplaying-server:twitch:state:{state}"))
                    return new APIResult(ResultStatusCode.BadRequest, "該 state 已被使用，請刷新後重試").ToContextResult();

                await _redisService.RedisDb.StringSetAsync($"nowplaying-server:twitch:state:{state}", "0", TimeSpan.FromMinutes(15));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OAuthCallBack - Redis 設定錯誤\n");
                return new APIResult(ResultStatusCode.InternalServerError, "伺服器內部錯誤，請向孤之界回報").ToContextResult();
            }

            return Redirect($"https://id.twitch.tv/oauth2/authorize" +
                $"?response_type=code" +
                $"&client_id={_configuration["Twitch:ClientId"]}" +
                $"&redirect_uri={_configuration["RedirectUrl"]}" +
                $"&scope=user:bot+user:read:chat+user:write:chat" +
                $"&state={state}");
        }

        public async Task<APIResult> OAuthCallBack(string state, string code = "", string error = "", string error_description = "")
        {
            if (!string.IsNullOrEmpty(error) && !string.IsNullOrEmpty(error_description))
                return new APIResult(ResultStatusCode.Unauthorized, $"使用者取消授權或是授權失敗\n{error_description.Replace("+", " ")}");

            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
                return new APIResult(ResultStatusCode.BadRequest, "參數錯誤");

            try
            {
                if (!await _redisService.RedisDb.KeyExistsAsync($"nowplaying-server:twitch:state:{state}"))
                    return new APIResult(ResultStatusCode.BadRequest, "該 state 並未使用過，請重新執行登入流程");

                if (await _redisService.RedisDb.KeyExistsAsync($"nowplaying-server:twitch:code:{code}"))
                    return new APIResult(ResultStatusCode.BadRequest, "請確認是否有插件或軟體導致重複驗證\n如網頁正常顯示資料則無需理會");

                await _redisService.RedisDb.StringSetAsync($"nowplaying-server:twitch:code:{code}", "0", TimeSpan.FromHours(1));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OAuthCallBack - Redis 設定錯誤\n");
                return new APIResult(ResultStatusCode.InternalServerError, "伺服器內部錯誤，請向孤之界回報");
            }

            try
            {
                var authCodeResponse = await _twitchAPI.Auth.GetAccessTokenFromCodeAsync(code, _configuration["Twitch:ClientSecret"], _configuration["RedirectUrl"]);
                if (authCodeResponse == null)
                    return new APIResult(ResultStatusCode.Unauthorized, "請重新登入 Twitch 帳號");

                if (string.IsNullOrEmpty(authCodeResponse.AccessToken))
                    return new APIResult(ResultStatusCode.Unauthorized, "Twitch 授權驗證無效\n請解除應用程式授權後再登入 Twitch 帳號");

                if (!string.IsNullOrEmpty(authCodeResponse.RefreshToken))
                {
                    var twitchAccessTokenData = new TwitchAccessTokenData()
                    {
                        AccessToken = authCodeResponse.AccessToken,
                        RefreshToken = authCodeResponse.RefreshToken,
                        ExpiresIn = authCodeResponse.ExpiresIn,
                        Scopes = authCodeResponse.Scopes,
                        TokenType = authCodeResponse.TokenType,
                    };

                    TwitchLib.Api.Helix.Models.Users.GetUsers.GetUsersResponse userData = null;
                    try
                    {
                        userData = await _twitchAPI.Helix.Users.GetUsersAsync(accessToken: twitchAccessTokenData.AccessToken);
                    }
                    catch (Exception ex)
                    {
                        if (ex.Message.Contains("401") || ex.Message.Contains("403"))
                        {
                            _logger.LogError(ex, "OAuthCallBack - 401 or 403 錯誤\n");
                            return new APIResult(ResultStatusCode.Unauthorized, "請嘗試重新登入 Twitch 帳號");
                        }
                        else
                        {
                            _logger.LogError(ex, "OAuthCallBack - Twitch API 回傳錯誤\n");
                            return new APIResult(ResultStatusCode.InternalServerError, "伺服器內部錯誤，請向孤之界回報");
                        }
                    }

                    if (userData == null)
                    {
                        _logger.LogWarning("OAuthCallBack - 無法取得資料\n");
                        return new APIResult(ResultStatusCode.BadRequest, "無法取得資料，請向孤之界回報");
                    }

                    var user = userData.Users.First();

                    var encValue = _tokenService.CreateTokenResponseToken(twitchAccessTokenData);
                    await _redisService.RedisDb.StringSetAsync(new($"nowplaying-server:twitch:oauth:{user.Id}"), encValue);

                    string token = "";
                    try
                    {
                        token = _tokenService.CreateToken(user.Id);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogCritical(ex, "OAuthCallBack - 建立 JWT 錯誤\n");
                        return new APIResult(ResultStatusCode.InternalServerError, "伺服器內部錯誤，請向孤之界回報");
                    }

                    return new APIResult(ResultStatusCode.OK, new { token });
                }
                else
                {
                    return new APIResult(ResultStatusCode.Unauthorized, "無法刷新 Twitch 授權\n請重新登入 Twitch 帳號");
                }
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("invalid_grant"))
                {
                    _logger.LogWarning("偵測到 invalid_grant");
                    return new APIResult(ResultStatusCode.Unauthorized, "Twitch 授權驗證無效或尚未登入\n請解除應用程式授權後再登入 Twitch 帳號");
                }
                else
                {
                    _logger.LogError(ex, "OAuthCallBack - 整體錯誤\n");
                    return new APIResult(ResultStatusCode.InternalServerError, "伺服器內部錯誤，請向孤之界回報");
                }
            }
        }

        [EnableCors("allowGET")]
        [HttpGet]
        public async Task<APIResult> GetTwitchData(string token = "")
        {
            if (string.IsNullOrEmpty(token))
                return new APIResult(ResultStatusCode.BadRequest, "Token 不可為空");

            try
            {
                var userId = _tokenService.GetUser<string>(token);
                if (string.IsNullOrEmpty(userId))
                    return new APIResult(ResultStatusCode.Unauthorized, "Token 無效");

                var twitchTokenEnc = await _redisService.RedisDb.StringGetAsync(new RedisKey($"nowplaying-server:twitch:oauth:{userId}"));
                if (!twitchTokenEnc.HasValue)
                    return new APIResult(ResultStatusCode.Unauthorized, "請登入 Twitch 帳號");

                var twitchToken = _tokenService.GetTokenResponseValue<TwitchAccessTokenData>(twitchTokenEnc);
                if (string.IsNullOrEmpty(twitchToken.AccessToken))
                    return new APIResult(ResultStatusCode.Unauthorized, "Twitch 授權驗證無效\n請解除應用程式授權後再登入 Twitch 帳號");

                if (string.IsNullOrEmpty(twitchToken.RefreshToken))
                    return new APIResult(ResultStatusCode.Unauthorized, "無法刷新 Twitch 授權\n請重新登入 Twitch 帳號");

                try
                {
                    if (await _twitchAPI.Auth.ValidateAccessTokenAsync(twitchToken.AccessToken) == null)
                    {
                        var refreshResponse = await _twitchAPI.Auth.RefreshAuthTokenAsync(twitchToken.RefreshToken, _configuration["Twitch:ClientSecret"]);

                        twitchToken.AccessToken = refreshResponse.AccessToken;
                        twitchToken.RefreshToken = refreshResponse.RefreshToken;
                        twitchToken.ExpiresIn = refreshResponse.ExpiresIn;
                        twitchToken.Scopes = refreshResponse.Scopes;

                        var encValue = _tokenService.CreateTokenResponseToken(refreshResponse);
                        await _redisService.RedisDb.StringSetAsync(new($"nowplaying-server:twitch:oauth:{userId}"), encValue);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "GetTwitchData - 刷新 Token 錯誤\n");
                    return new APIResult(ResultStatusCode.Unauthorized, "無法刷新 Twitch 授權\n請重新登入 Twitch 帳號");
                }

                return new APIResult(ResultStatusCode.OK, new { access_token = twitchToken.AccessToken, client_id = _configuration["Twitch:ClientId"], user_id = userId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetTwitchData - 整體錯誤\n");
                return new APIResult(ResultStatusCode.InternalServerError, "伺服器內部錯誤，請向孤之界回報");
            }
        }
    }
}
