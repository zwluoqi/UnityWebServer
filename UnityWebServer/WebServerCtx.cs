using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using HTTPServerLib;
using UnityEngine;

namespace UnityWebServer
{

	public class WebServerCtx : MonoBehaviour
	{
		public int port = 19527;
		public IPAddress listen_ip = Dns.GetHostEntry("localhost").AddressList[0];

		//public float ListenDelay = 0.1f;
		private WaitForEndOfFrame wait = new WaitForEndOfFrame();

		private string local_public_html_url;

		/// <summary>
		/// SSL证书
		/// </summary>
		private X509Certificate serverCertificate = null;

		void Start()
		{
			Application.runInBackground = true;
			local_public_html_url = Application.streamingAssetsPath;
			local_public_html_url += "/flash_html/";


			local_public_html_url = Path.GetFullPath(local_public_html_url);
			local_public_html_url =
				local_public_html_url.Substring(0, local_public_html_url.Length - 1); //remove last /
			// local_public_html_url = "file://"+local_public_html_url;

			Debug.Log("Starting server. html files should be at " + local_public_html_url);

			StartCoroutine(listen(listen_ip, port));
		}

		// rotate just to check if it hangs...
		public float speed = 50f;

		void Update()
		{
			transform.Rotate(Vector3.up * Time.deltaTime * speed);
		}

		private IEnumerator listen(IPAddress listen_ip, int port)
		{
			TcpListener listener = new TcpListener(listen_ip, port);
			listener.Start();
			while (gameObject.activeSelf)
			{

				if (listener.Pending())
				{
					TcpClient client = listener.AcceptTcpClient();
					//Debug.Log("got a client! create a HttpProcessor for him and start the process coroutine");
					// HttpProcessor processor = new HttpProcessor(s, this);
					// StartCoroutine(processor.process());
					Thread requestThread = new Thread(() => { ProcessRequest(client); });
					requestThread.Start();
				}

				yield return wait;
				//yield return new WaitForSeconds(ListenDelay);
			}
		}

		public void handleGetRequest(HttpRequest p, HttpResponse response)
		{
			Debug.Log("Handling request: " + p.URL);

			string localFileUrl = p.URL;
			if (localFileUrl.EndsWith("/"))
			{
				localFileUrl = localFileUrl + "/templates/index.html";
			}

			localFileUrl = local_public_html_url + localFileUrl;

			// WWW www = new WWW(localFileUrl);
			//
			// yield return www;
			// var content = File.ReadAllBytes(localFileUrl);

			if (!File.Exists(localFileUrl))
			{
				response.SetContent("<html><body><h1>404 - Not Found</h1></body></html>");
				response.StatusCode = "404";
				response.Content_Type = "text/html";
				//发送响应
				response.Send();
			}

			var content = File.ReadAllBytes(localFileUrl);
			{
				//构造响应报文
				response.SetContent(content);
				response.Content_Encoding = "utf-8";
				response.StatusCode = "200";

				response.Headers["Server"] = "ExampleServer";

				if (p.URL.EndsWith(".ico"))
				{
					// p.writeSuccess(content_type:"image/x-icon");
					response.Content_Type = "image/x-icon; charset=UTF-8";

				}
				else if (p.URL.EndsWith(".png"))
				{
					// p.writeSuccess(content_type:"image/png");
					response.Content_Type = "image/png; charset=UTF-8";

				}
				else if (p.URL.EndsWith(".jpg") || p.URL.EndsWith("jpeg"))
				{
					// p.writeSuccess(content_type:"image/jpeg");
					response.Content_Type = "image/jpeg; charset=UTF-8";

				}
				else if (p.URL.EndsWith(".js"))
				{
					// p.writeSuccess(content_type:"application/x-javascript");
					response.Content_Type = "application/x-javascript; charset=UTF-8";

				}
				else if (p.URL.EndsWith(".css"))
				{
					// p.writeSuccess(content_type:"text/css");
					response.Content_Type = "text/css; charset=UTF-8";
				}
				else
				{
					// p.writeSuccess();
				}
				// p.netStream.Write(www.bytes, 0, www.bytes.Length);

				//发送响应
				response.Send();

			}
			//p.writeLine("<html><body><h1>unity web server</h1></br><img src=\"bear.png\" alt=\"Bear\">");
		}


		/// <summary>
		/// 处理客户端请求
		/// </summary>
		/// <param name="handler">客户端Socket</param>
		private void ProcessRequest(TcpClient handler)
		{
			//处理请求
			Stream clientStream = handler.GetStream();

			//处理SSL
			if (serverCertificate != null) clientStream = ProcessSSL(clientStream);
			if (clientStream == null) return;

			try
			{
				//构造HTTP请求
				HttpRequest request = new HttpRequest(clientStream);
				// request.Logger = Logger;

				//构造HTTP响应
				HttpResponse response = new HttpResponse(clientStream);
				// response.Logger = Logger;


				//处理请求类型
				switch (request.Method)
				{
					case "GET":
						OnGet(request, response);
						break;
					case "POST":
						OnPost(request, response);
						break;
					default:
						OnDefault(request, response);
						break;
				}
			}
			catch (Exception e)
			{
				Debug.LogError(e.ToString());
			}
		}


		private void OnDefault(HttpRequest request, HttpResponse response)
		{
			throw new NotImplementedException();
		}

		private void OnPost(HttpRequest request, HttpResponse response)
		{
			//获取客户端传递的参数
			string data = request.Params == null
				? ""
				: string.Join(";", request.Params.Select(x => x.Key + "=" + x.Value).ToArray());

			//上传文件列表
			foreach (var kv in request.UploadFile)
			{
				Debug.LogWarning($"接收文件:{kv.Key}");
			}

			//设置返回信息
			string content = string.Format("这是通过Post方式返回的数据:{0}", data);

			//构造响应报文
			response.SetContent(content);
			response.Content_Encoding = "utf-8";
			response.StatusCode = "200";
			response.Content_Type = "text/html; charset=UTF-8";
			response.Headers["Server"] = "ExampleServer";

			//发送响应
			response.Send();
		}

		private void OnGet(HttpRequest request, HttpResponse response)
		{
			handleGetRequest(request, response);
		}


		/// <summary>
		/// 处理ssl加密请求
		/// </summary>
		/// <param name="clientStream"></param>
		/// <returns></returns>
		private Stream ProcessSSL(Stream clientStream)
		{
			try
			{
				SslStream sslStream = new SslStream(clientStream);
				sslStream.AuthenticateAsServer(serverCertificate, false, SslProtocols.Tls, true);
				sslStream.ReadTimeout = 10000;
				sslStream.WriteTimeout = 10000;
				return sslStream;
			}
			catch (Exception e)
			{
				Debug.LogError(e.Message);
				clientStream.Close();
			}

			return null;
		}

	}

}