using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Xml.Linq;

namespace Dulcet.Network
{
    public static class Http
    {
        private static string _userAgent = "Krile2/Mystique (.NET Framework 4.0)";
        public static string UserAgent
        {
            get { return _userAgent; }
            set { _userAgent = value; }
        }

        private static int _timeoutInterval = 20000;
        public static int TimeoutInterval
        {
            get { return _timeoutInterval; }
            set { _timeoutInterval = value; }
        }

        /// <summary>
        /// Use expect100continue
        /// </summary>
        public static bool Expect100Continue
        {
            get { return ServicePointManager.Expect100Continue; }
            set { ServicePointManager.Expect100Continue = value; }
        }

        /// <summary>
        /// HTTP parallel connection limit
        /// </summary>
        public static int MaxConnectionLimit
        {
            get { return ServicePointManager.DefaultConnectionLimit; }
            set { ServicePointManager.DefaultConnectionLimit = value; }
        }

        static Http()
        {
            // Windows 8.1 Preview
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls;
        }

        #region Converter delegate definitions and common converters

        /// <summary>
        /// Stream convert to preferred class
        /// </summary>
        public delegate T StreamConverter<out T>(Stream stream);

        /// <summary>
        /// Response convert to preferred class
        /// </summary>
        public delegate T ResponseConverter<out T>(HttpWebResponse response);

        /// <summary>
        /// Common stream converters
        /// </summary>
        public static class StreamConverters
        {
            /// <summary>
            /// Read string from stream
            /// </summary>
            public static string ReadString(Stream stream)
            {
                using (var sr = new StreamReader(stream))
                    return sr.ReadToEnd();
            }

            /// <summary>
            /// Read stream as XML text
            /// </summary>
            public static XDocument ReadXml(Stream stream)
            {
                return XDocument.Load(stream);
            }

            /// <summary>
            /// Read stream and cache in memory
            /// </summary>
            /// <param name="stream">stream</param>
            /// <returns>memory stream</returns>
            public static MemoryStream ReadStream(Stream stream)
            {
                var ms = new MemoryStream();
                stream.CopyTo(ms);
                ms.Seek(0, SeekOrigin.Begin);
                return ms;
            }
        }

        /// <summary>
        /// Common response converters
        /// </summary>
        public static class ResponseConverters
        {
            /// <summary>
            /// Get raw stream
            /// </summary>
            public static Stream GetStream(HttpWebResponse res)
            {
                return res.GetResponseStream();
            }
        }

        #endregion

        /// <summary>
        /// Create HTTP request
        /// </summary>
        /// <param name="uri">HTTP uri</param>
        /// <param name="method">using method</param>
        /// <param name="contentType">content type</param>
        /// <param name="credential">using credential</param>
        /// <returns>request</returns>
        public static HttpWebRequest CreateRequest(
            Uri uri, string method = "GET",
            string contentType = null,
            ICredentials credential = null)
        {
            if (uri == null)
                throw new ArgumentNullException("uri");

            HttpWebRequest req = null;
            // create request
            try
            {
                req = WebRequest.Create(uri) as HttpWebRequest;
                if (req == null)
                    throw new NotSupportedException("Std.HttpWeb supports only HTTP.");
            }
            catch (NotSupportedException)
            {
                throw;
            }

            // set parameters
            req.UserAgent = UserAgent;
            req.Timeout = TimeoutInterval;
            req.AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip;

            // argument values
            req.ContentType = contentType;
            req.Method = method;
            req.Credentials = credential;
            return req;
        }


        /// <summary>
        /// Connect to web and download response
        /// </summary>
        /// <typeparam name="T">return type</typeparam>
        /// <param name="req">requestor</param>
        /// <param name="streamconv">stream converter</param>
        /// <param name="responseconv">response converter</param>
        /// <param name="senddata">sending data info</param>
        /// <returns>converted data</returns>
        /// <exception cref="System.ArgumentException"></exception>
        public static WebResult<T> WebConnect<T>(
            HttpWebRequest req,
            StreamConverter<T> streamconv = null,
            ResponseConverter<T> responseconv = null,
            byte[] senddata = null)
        {
            System.Diagnostics.Debug.WriteLine(req.Method + " Connect to " + req.RequestUri.OriginalString);
            if (!(streamconv == null ^ responseconv == null))
                throw new ArgumentException("StreamConverter or ResponseConverter is must set.");

            try
            {
                // upload data
                if (senddata != null && senddata.Length > 0)
                {
                    // content length
                    req.ContentLength = senddata.Length;

                    // request streams
                    using (var s = req.GetRequestStream())
                    {
                        s.Write(senddata, 0, senddata.Length);
                    }
                }
                else
                {
                    req.ContentLength = 0;
                }
                return TreatWebResponse((HttpWebResponse)req.GetResponse(), streamconv, responseconv);
            }
            catch (SocketException e)
            {
                return new WebResult<T>(req.RequestUri, e);
            }
            catch (IOException e)
            {
                return new WebResult<T>(req.RequestUri, e);
            }
            catch (WebException e)
            {
                return new WebResult<T>(req.RequestUri, e);
            }
        }

        /// <summary>
        /// Request with arguments(web form style).<para />
        /// (use POST/x-www-form-urlencoded)
        /// </summary>
        /// <typeparam name="T">return type</typeparam>
        /// <param name="req">request</param>
        /// <param name="dict">send arguments dictionary</param>
        /// <param name="encode">encoding</param>
        /// <param name="streamconv">stream converter</param>
        /// <param name="responseconv">response converter</param>
        /// <returns>WebResult</returns>
        public static WebResult<T> WebFormSendString<T>(
            HttpWebRequest req,
            IEnumerable<KeyValuePair<string, string>> dict,
            Encoding encode,
            StreamConverter<T> streamconv = null,
            ResponseConverter<T> responseconv = null)
        {
            var para = from k in dict
                       select k.Key + "=" + k.Value;

            var dat = encode.GetBytes(String.Join("&", para.ToArray()));
            req.Method = "POST"; // static
            req.ContentType = "application/x-www-form-urlencoded";
            return WebConnect(
                req,
                streamconv,
                responseconv,
                dat);
        }

        /// <summary>
        /// Request with .<para />
        /// (use POST/x-www-form-urlencoded)
        /// </summary>
        /// <typeparam name="T">return type</typeparam>
        /// <param name="req">request</param>
        /// <param name="sends">sending datas</param>
        /// <param name="encode">encoding</param>
        /// <param name="streamconv">stream converter</param>
        /// <param name="responseconv">response converter</param>
        /// <returns>WebResult</returns>
        public static WebResult<T> WebUpload<T>(
            HttpWebRequest req,
            IEnumerable<SendData> sends,
            Encoding encode,
            StreamConverter<T> streamconv = null,
            ResponseConverter<T> responseconv = null)
        {
            try
            {
                // boundary
                string boundary = Guid.NewGuid().ToString("N");
                string separator = "--" + boundary + "\r\n";

                req.Method = "POST";
                req.ContentType = "multipart/form-data; boundary=" + boundary;

                byte[] endsep = encode.GetBytes("\r\n--" + boundary + "--\r\n");
                long gross = 0;
                foreach (var s in sends)
                {
                    gross += s.GetDataLength(boundary, encode);
                }
                gross += endsep.Length;

                req.ContentLength = gross;
                gross = 0;
                using (var rs = req.GetRequestStream())
                {
                    foreach (var s in sends)
                    {
                        foreach (var i in s.EnumerateByte(boundary, encode))
                        {
                            rs.Write(i, 0, i.Length);
                            gross += i.Length;
                        }
                    }
                    rs.Write(endsep, 0, endsep.Length); // finalize
                    rs.Flush();
                }
                return TreatWebResponse((HttpWebResponse)req.GetResponse(), streamconv, responseconv);
            }
            catch (SocketException e)
            {
                return new WebResult<T>(req.RequestUri, e);
            }
            catch (IOException e)
            {
                return new WebResult<T>(req.RequestUri, e);
            }
            catch (WebException e)
            {
                if (e.Response != null && e.Response.ContentLength > 0)
                {
                    using (var sr = new StreamReader(e.Response.GetResponseStream()))
                    {
                        System.Diagnostics.Debug.WriteLine(sr.ReadToEnd());
                    }
                    e.Response.Close();
                }
                return new WebResult<T>(req.RequestUri, e);
            }
        }

        /// <summary>
        /// Simply post a daata
        /// </summary>
        public static WebResult<T> WebSimplePost<T>(
            HttpWebRequest req,
            byte[] sendbytes,
            StreamConverter<T> streamconv = null,
            ResponseConverter<T> responseconv = null)
        {
            try
            {
                req.Method = "POST";
                req.ContentLength = sendbytes.Length;
                using (var rs = req.GetRequestStream())
                {
                    rs.Write(sendbytes, 0, sendbytes.Length);
                    rs.Flush();
                }
                return TreatWebResponse((HttpWebResponse)req.GetResponse(), streamconv, responseconv);
            }
            catch (SocketException e)
            {
                return new WebResult<T>(req.RequestUri, e);
            }
            catch (IOException e)
            {
                return new WebResult<T>(req.RequestUri, e);
            }
            catch (WebException e)
            {
                return new WebResult<T>(req.RequestUri, e);
            }
        }

        /// <summary>
        /// Parse web response
        /// </summary>
        private static WebResult<T> TreatWebResponse<T>
            (HttpWebResponse res, StreamConverter<T> strconv, ResponseConverter<T> resconv)
        {
            if (!(strconv == null ^ resconv == null))
                throw new ArgumentException("StreamConverter or ResponseConverter is must set.");
            try
            {
                switch (res.StatusCode)
                {
                    case HttpStatusCode.OK:
                    case HttpStatusCode.Accepted:
                    case HttpStatusCode.Created:
                    case HttpStatusCode.NoContent:
                    case HttpStatusCode.ResetContent:
                    case HttpStatusCode.PartialContent:
                        if (resconv != null)
                        {
                            return new WebResult<T>(
                                res.ResponseUri,
                                res.StatusCode,
                                resconv(res));
                        }
                        else if (strconv != null)
                        {
                            using (var s = res.GetResponseStream())
                            {
                                return new WebResult<T>(
                                    res.ResponseUri,
                                    res.StatusCode,
                                    strconv(s));
                            }
                        }
                        else
                        {
                            throw new InvalidOperationException("StreamConverter or ResponseConverter is must set.");
                        }

                    default:
                        return new WebResult<T>(
                            res.ResponseUri,
                            null,
                            res.StatusCode);
                }
            }
            catch (Exception e)
            {
                return new WebResult<T>(
                    res.ResponseUri, e);
            }
        }

        /// <summary>
        /// Connect specified uri and download string
        /// </summary>
        /// <param name="req">request parameter</param>
        public static WebResult<string> WebConnectDownloadString(
            HttpWebRequest req)
        {
            return WebConnect(
                req,
                new StreamConverter<string>(StreamConverters.ReadString));
        }

        /// <summary>
        /// Connect specified uri and download string
        /// </summary>
        public static WebResult<string> WebConnectDownloadString(
            Uri uri, string method = "GET", ICredentials credential = null, string contentType = null)
        {
            return WebConnectDownloadString(
                CreateRequest(uri, method, credential: credential, contentType: contentType));
        }

    }

    /// <summary>
    /// Sending data descriptor structure
    /// </summary>
    public struct SendData
    {
        private string name;

        /// <summary>
        /// Field name
        /// </summary>
        public string Name
        {
            get { return name; }
            set
            {
                if (value == name) return;
                name = value;
                cache = null;
            }
        }

        private string text;

        /// <summary>
        /// Argument text
        /// </summary>
        public string Text
        {
            get { return text; }
            set
            {
                if (value == null)
                    throw new ArgumentNullException();
                if (value == text) return;
                text = value;
                fpath = null;
                cache = null;
            }
        }

        private string fpath;

        /// <summary>
        /// Argument file's path
        /// </summary>
        public string FilePath
        {
            get { return fpath; }
            set
            {
                if (value == null)
                    throw new ArgumentNullException();
                if (value == fpath) return;
                fpath = value;
                text = null;
                cache = null;
            }
        }

        /// <summary>
        /// Create text item
        /// </summary>
        /// <param name="name">field name</param>
        /// <param name="value">argument value</param>
        public SendData(string name, string value)
            : this()
        {
            this.Name = name;
            this.Text = value;
        }

        /// <summary>
        /// Create data item<para/>
        /// You must set text property or file property.
        /// </summary>
        /// <param name="name">field name</param>
        /// <param name="text">argument text</param>
        /// <param name="file">argument file</param>
        public SendData(string name, string text = null, string file = null)
            : this()
        {
            if (!(text == null ^ file == null))
                throw new ArgumentException("Specified value is not setted or setted excess.");

            this.Name = name;
            if (text != null)
                this.Text = text;
            if (file != null)
                this.FilePath = file;
        }

        private Encoding cacheenc;
        private string cacheboundary;
        private byte[] cache;
        /// <summary>
        /// Get byte data length
        /// </summary>
        public long GetDataLength(string boundary, Encoding encode)
        {
            UpdateCache(boundary, encode);
            if (text != null)
            {
                return cache.Length;
            }
            else if (fpath != null)
            {
                if (!File.Exists(fpath))
                    throw new FileNotFoundException("File not found.", fpath);
                using (var fs =
                    new FileStream(
                        fpath, FileMode.Open, FileAccess.Read))
                {
                    return fs.Length + cache.Length;
                }
            }
            else
            {
                throw new InvalidOperationException("Internal arguments are null reference.");
            }
        }

        /// <summary>
        /// Enumerate bytes array
        /// </summary>
        public IEnumerable<byte[]> EnumerateByte(string boundary, Encoding encode)
        {
            UpdateCache(boundary, encode);
            if (text != null)
            {
                yield return cache;
            }
            else if (fpath != null)
            {
                yield return cache; // write boundary

                long fsize = 0;
                using (var fs = new FileStream(fpath, FileMode.Open, FileAccess.Read))
                {
                    byte[] rdata = new byte[0x1000];
                    int rsize = 0;
                    for (; ; )
                    {
                        rsize = fs.Read(rdata, 0, rdata.Length);
                        fsize += rsize;
                        if (rsize <= 0)
                            break;
                        if (rsize == rdata.Length)
                            yield return rdata;
                        else // slice
                            yield return rdata.Take(rsize).ToArray();
                    }
                }
            }
            else
            {
                throw new InvalidOperationException("Internal arguments are null reference.");
            }
        }

        /// <summary>
        /// Update internal cache
        /// </summary>
        private void UpdateCache(string boundary, Encoding encode)
        {
            if (cache == null || cacheenc != encode || cacheboundary != boundary)
            {
                var separator = "--" + boundary + "\r\n";
                StringBuilder sb = new StringBuilder();
                //generate byte cache
                if (text != null)
                {
                    sb.Append(separator +
                        "Content-Disposition: form-data; name=\"" +
                        this.name +
                        "\"\r\n\r\n");
                    sb.Append(this.text + "\r\n");
                }
                else if (fpath != null)
                {
                    sb.Append(separator +
                            "Content-Disposition: form-data; name=\"" +
                            this.name +
                            "\"; filename=\"" +
                            Path.GetFileName(this.fpath) +
                            "\"\r\n");
                    sb.Append("Content-Type: application/octet-stream\r\n");
                    sb.Append("Content-Transfer-Encoding: binary\r\n\r\n");
                }
                else
                {
                    throw new InvalidOperationException("Internal arguments are null reference.");
                }
                cache = encode.GetBytes(sb.ToString());
                cacheboundary = boundary;
                cacheenc = encode;
            }
        }
    }
}