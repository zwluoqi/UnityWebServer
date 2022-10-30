using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.IO;
using System.Text.RegularExpressions;
using HttpMultipartParser;

namespace HTTPServerLib
{
    /// <summary>
    /// HTTP请求定义
    /// </summary>
    public class HttpRequest : BaseHeader
    {
        /// <summary>
        /// URL参数
        /// </summary>
        public Dictionary<string, string> Params { get; private set; }

        public Dictionary<string, string> UploadFile { get; private set; }

        /// <summary>
        /// HTTP请求方式
        /// </summary>
        public string Method { get; private set; }

        /// <summary>
        /// HTTP(S)地址
        /// </summary>
        public string URL { get; set; }

        /// <summary>
        /// HTTP协议版本
        /// </summary>
        public string ProtocolVersion { get; set; }

        /// <summary>
        /// 定义缓冲区
        /// </summary>
        private const int MAX_SIZE = 1024 * 1024 * 2;
        private byte[] bytes = new byte[MAX_SIZE];

        public ILogger Logger { get; set; }

        private Stream handler;

        public HttpRequest(Stream stream)
        {
            this.handler = stream;
            var header = GetRequestData(handler,out var headerByteLength,out var readByteLength);
            var rows = Regex.Split(header, Environment.NewLine);

            //Request URL & Method & Version
            var first = Regex.Split(rows[0], @"(\s+)")
                .Where(e => e.Trim() != string.Empty)
                .ToArray();
            if (first.Length > 0) this.Method = first[0];
            if (first.Length > 1) this.URL = Uri.UnescapeDataString(first[1]);
            if (first.Length > 2) this.ProtocolVersion = first[2];

            //Request Headers
            this.Headers = GetRequestHeaders(rows);
            var contentType = GetHeader(RequestHeaders.ContentType);
            bool isMultipart = false;
            if (contentType != null)
            {
                isMultipart = contentType.StartsWith(@"multipart/form-data");
            }
            
            //Request Body
            // Body = GetRequestBody(rows);
            var readBodyByteLength = readByteLength - headerByteLength;
            var contentLength = GetHeader(RequestHeaders.ContentLength);
            int.TryParse(contentLength, out var needBodyByteLength);
            
            if (isMultipart)
            {

                using (MemoryStream cahceStream = new MemoryStream(MAX_SIZE * 8))
                {
                    cahceStream.Write(bytes, headerByteLength, readByteLength - headerByteLength);
                    if (readBodyByteLength != needBodyByteLength)
                    {
                        do
                        {
                            var length = stream.Read(bytes, 0, MAX_SIZE - 1);
                            cahceStream.Write(bytes, 0, length);
                            readBodyByteLength += length;
                        } while (readBodyByteLength != needBodyByteLength);
                    }

                    cahceStream.Seek(0, SeekOrigin.Begin);

                    // using (MemoryStream streamReader = new MemoryStream(cahceStream))
                    {
                        // ==== Advanced Parsing ====
                        var parser = new StreamingMultipartFormDataParser(cahceStream);
                        Dictionary<string, FileStream> filestreamsByName = new Dictionary<string, FileStream>();
                        UploadFile = new Dictionary<string, string>();
                        // parser.ParameterHandler += parameter => DoSomethingWithParameter(parameter);
                        parser.FileHandler += (name, fileName, type, disposition, buffer, bytes, partNumber,
                            additionalProperties) =>
                        {
                            // Write the part of the file we've received to a file stream. (Or do something else)
                            // Assume that filesreamsByName is a Dictionary<string, FileStream> of all the files
                            // we are writing.
                            if (!filestreamsByName.TryGetValue(fileName, out var fileStream))
                            {
                                fileStream = new FileStream(fileName, FileMode.OpenOrCreate);
                                filestreamsByName[fileName] = fileStream;
                                UploadFile[fileName] = fileName;
                            }

                            fileStream.Write(buffer, 0, bytes);
                        };
                        parser.StreamClosedHandler += () =>
                        {
                            // Do things when my input stream is closed
                            foreach (var kv in filestreamsByName)
                            {
                                kv.Value.Flush();
                            }
                        };

// You can parse synchronously:
                        parser.Run();
                    }
                    
                }
            }
            else
            {
                if ( readBodyByteLength != needBodyByteLength)
                {
                    do
                    {
                        var length = stream.Read(bytes, 0, MAX_SIZE - 1);
                        Body += Encoding.UTF8.GetString(bytes, 0, length);
                        readBodyByteLength += length;
                    } while (readBodyByteLength != needBodyByteLength);
                } 
            }

            //Request "GET"
            if (this.Method == "GET")
            {
                var isUrlencoded = this.URL.Contains('?');
                if (isUrlencoded) this.Params = GetRequestParameters(URL.Split('?')[1]);
            }

            //Request "POST"
            if (this.Method == "POST")
            {
                var isUrlencoded = contentType == @"application/x-www-form-urlencoded";
                if (isUrlencoded) this.Params = GetRequestParameters(this.Body);
            }
        }

        public Stream GetRequestStream()
        {
            return this.handler;
        }

        public string GetHeader(RequestHeaders header)
        {
            return GetHeaderByKey(header);
        }

        public string GetHeader(string fieldName)
        {
            return GetHeaderByKey(fieldName);
        }

        public void SetHeader(RequestHeaders header, string value)
        {
            SetHeaderByKey(header, value);
        }

        public void SetHeader(string fieldName,string value)
        {
            SetHeaderByKey(fieldName, value);
        }

        private string GetRequestData(Stream stream,out int headerByteLength,out int readByteLength)
        {
            var length = 0;
            headerByteLength = 0;
            readByteLength = 0;
            
            length = stream.Read(bytes, 0, MAX_SIZE - 1);
            readByteLength += length;
            var data = Encoding.UTF8.GetString(bytes, 0, length);
            
            var headerSplit = data.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            var header = data.Substring(0, headerSplit  + 4);
            Body = data.Substring(headerSplit + 4);
            headerByteLength = Encoding.UTF8.GetBytes(header).Length;

            return header;
        }

        private string GetRequestBody(IEnumerable<string> rows)
        {
            var target = rows.Select((v, i) => new { Value = v, Index = i }).FirstOrDefault(e => e.Value.Trim() == string.Empty);
            if (target == null) return null;
            var range = Enumerable.Range(target.Index + 1, rows.Count() - target.Index - 1);
            return string.Join(Environment.NewLine, range.Select(e => rows.ElementAt(e)).ToArray());
        }

        private Dictionary<string, string> GetRequestHeaders(IEnumerable<string> rows)
        {
            if (rows == null || rows.Count() <= 0) return null;
            var target = rows.Select((v, i) => new { Value = v, Index = i }).FirstOrDefault(e => e.Value.Trim() == string.Empty);
            var length = target == null ? rows.Count() - 1 : target.Index;
            if (length <= 1) return null;
            var range = Enumerable.Range(1, length - 1);
            return range.Select(e => rows.ElementAt(e)).ToDictionary(e => e.Split(':')[0], e => e.Split(':')[1].Trim());
        }

        private Dictionary<string, string> GetRequestParameters(string row)
        {
            if (string.IsNullOrEmpty(row)) return null;
            var kvs = Regex.Split(row, "&");
            if (kvs == null || kvs.Count() <= 0) return null;

            return kvs.ToDictionary(e => Regex.Split(e, "=")[0], e => { var p = Regex.Split(e, "="); return p.Length > 1 ? p[1] : ""; });
        }
    }
}
