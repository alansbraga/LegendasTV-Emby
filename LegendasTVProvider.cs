using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using HtmlAgilityPack;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Security;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Providers;
using MediaBrowser.Model.Serialization;

namespace LegendasTV
{
    class LegendasTVProvider : ISubtitleProvider, IDisposable
    {
        public static readonly string URL_BASE = "http://legendas.tv/";
        public static readonly string URL_SEARCH = URL_BASE + "legenda/busca/{0}/{1}/-/0/-";
        public static readonly string URL_DOWNLOAD = URL_BASE + "info.php?d=%s&c=1";
        public static readonly string URL_LOGIN = URL_BASE + "login";

        private readonly ILogger _logger;
        private readonly IHttpClient _httpClient;
        private readonly IServerConfigurationManager _config;
        private readonly IEncryptionManager _encryption;
        private readonly IJsonSerializer _json;
        private readonly IFileSystem _fileSystem;
        private readonly ILibraryManager _libraryManager;
        private readonly ILocalizationManager _localizationManager;

        private const string PasswordHashPrefix = "h:";

        public string Name => "Legendas TV";


        public LegendasTVProvider(ILogger logger, IHttpClient httpClient, IServerConfigurationManager config, IEncryptionManager encryption, IJsonSerializer json, IFileSystem fileSystem, ILocalizationManager localizationManager, ILibraryManager libraryManager)
        {
            _logger = logger;
            _httpClient = httpClient;
            _config = config;
            _encryption = encryption;
            _json = json;
            _fileSystem = fileSystem;
            _libraryManager = libraryManager;
            _localizationManager = localizationManager;

            _config.NamedConfigurationUpdating += _config_NamedConfigurationUpdating;

            // Load HtmlAgilityPack from embedded resource
            EmbeddedAssembly.Load(GetType().Namespace + ".HtmlAgilityPack.dll", "HtmlAgilityPack.dll");
            AppDomain.CurrentDomain.AssemblyResolve += new ResolveEventHandler((object sender, ResolveEventArgs args) => EmbeddedAssembly.Get(args.Name));
        }

        private LegendasTVOptions GetOptions() => _config.GetLegendasTVConfiguration();

        public IEnumerable<VideoContentType> SupportedMediaTypes => new[] { VideoContentType.Episode, VideoContentType.Movie };

        public async Task<SubtitleResponse> GetSubtitles(string id, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public async Task<IEnumerable<RemoteSubtitleInfo>> Search(SubtitleSearchRequest request, CancellationToken cancellationToken)
        {
            if (await Login())
            {
                var query = "";
                switch (request.ContentType)
                {
                    case VideoContentType.Episode:
                        query = String.Format("{0} S{1:D2}E{2:D3}", request.SeriesName, request.ParentIndexNumber, request.IndexNumber);
                        break;
                    case VideoContentType.Movie:
                        BaseItem item = _libraryManager.FindByPath(request.MediaPath, false);
                        query = item.OriginalTitle;
                        break;
                }
                var lang = "-"; // TODO: Get language code
                var requestOptions = new HttpRequestOptions()
                {
                    Url = string.Format(URL_SEARCH, HttpUtility.HtmlEncode(query).Replace(" ", "+"), lang),
                    CancellationToken = cancellationToken
                };

                using (var stream = await _httpClient.Get(requestOptions))
                {
                    using (var reader = new StreamReader(stream))
                    {
                        var response = reader.ReadToEnd();
                        // TODO: parse resulting HTML
                        return Array.Empty<RemoteSubtitleInfo>();
                    }
                }
            }
            else
            {
                return Array.Empty<RemoteSubtitleInfo>();
            }
        }

        public async Task<bool> Login()
        {
            var options = GetOptions();
            var username = options.LegendasTVUsername;
            var password = DecryptPassword(options.LegendasTVPasswordHash);
            string result = await SendPost(URL_LOGIN, string.Format("data[User][username]={0}&data[User][password]={1}", username, password));

            if (result.Contains("Usuário ou senha inválidos"))
            {
                _logger.Error("Invalid username or password");
                return false;
            }

            return true;
        }

        private async Task<string> SendPost(string url, string postData)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(postData);

            var requestOptions = new HttpRequestOptions()
            {
                Url = url,
                RequestContentType = "application/x-www-form-urlencoded",
                RequestContentBytes = bytes
            };

            var response = await _httpClient.Post(requestOptions);

            using (var stream = response.Content)
            {
                using (var reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        void _config_NamedConfigurationUpdating(object sender, ConfigurationUpdateEventArgs e)
        {
            if (!string.Equals(e.Key, "legendastv", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var options = (LegendasTVOptions)e.NewConfiguration;

            if (options != null &&
                !string.IsNullOrWhiteSpace(options.LegendasTVPasswordHash) &&
                !options.LegendasTVPasswordHash.StartsWith(PasswordHashPrefix, StringComparison.OrdinalIgnoreCase))
            {
                options.LegendasTVPasswordHash = EncryptPassword(options.LegendasTVPasswordHash);
            }
        }

        private string EncryptPassword(string password)
        {
            return PasswordHashPrefix + _encryption.EncryptString(password);
        }

        private string DecryptPassword(string password)
        {
            if (password == null ||
                !password.StartsWith(PasswordHashPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return _encryption.DecryptString(password.Substring(2));
        }

        public void Dispose() => GC.SuppressFinalize(this);

        ~LegendasTVProvider() => Dispose();
    }
}