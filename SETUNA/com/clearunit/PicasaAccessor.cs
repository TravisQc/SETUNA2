using System;
using System.Collections;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Text;
using System.Xml;
using SETUNA.Main.Localization;

namespace com.clearunit
{
    // Token: 0x02000076 RID: 118
    internal class PicasaAccessor
    {
        // Token: 0x060003E4 RID: 996 RVA: 0x00018020 File Offset: 0x00016220
        public PicasaAccessor()
        {
            Auth = "";
            LoginErrorCode = PicasaLoginError.None;
        }

        // Token: 0x17000099 RID: 153
        // (get) Token: 0x060003E5 RID: 997 RVA: 0x0001803A File Offset: 0x0001623A
        private string LoginUrl => "https://www.google.com/accounts/ClientLogin";

        // Token: 0x1700009A RID: 154
        // (get) Token: 0x060003E6 RID: 998 RVA: 0x00018041 File Offset: 0x00016241
        private string Url => "https://picasaweb.google.com/data/feed/api/user/" + ID;

        // Token: 0x1700009B RID: 155
        // (get) Token: 0x060003E7 RID: 999 RVA: 0x00018053 File Offset: 0x00016253
        private string AlbumFeedUrl => "https://picasaweb.google.com/data/feed/api/user/" + ID + "?kind=album";

        // Token: 0x1700009C RID: 156
        // (get) Token: 0x060003E8 RID: 1000 RVA: 0x0001806A File Offset: 0x0001626A
        private string UploadUrl => "https://picasaweb.google.com/data/feed/api/user/" + ID + "/album/";

        // Token: 0x060003E9 RID: 1001 RVA: 0x00018084 File Offset: 0x00016284
        public bool ClientLogin(string id, string pass)
        {
            ID = id;
            Auth = "";
            LoginErrorCode = PicasaLoginError.None;
            UploadErrorCode = PicasaUploadError.None;
            var result = false;
            var utf = Encoding.UTF8;
            var text = "";
            var hashtable = new Hashtable
            {
                ["accountType"] = "GOOGLE",
                ["Email"] = id,
                ["Passwd"] = pass,
                ["service"] = "lh2",
                ["source"] = "Clearup-PicasaAccessor-1.00"
            };
            foreach (var obj in hashtable.Keys)
            {
                var text2 = (string)obj;
                text += string.Format("{0}={1}&", text2, hashtable[text2]);
            }
            var bytes = Encoding.ASCII.GetBytes(text);
            var httpWebRequest = (HttpWebRequest)WebRequest.Create(LoginUrl);
            httpWebRequest.Method = "POST";
            httpWebRequest.ContentType = "application/x-www-form-urlencoded";
            httpWebRequest.ContentLength = bytes.Length;

            try
            {
                using (var requestStream = httpWebRequest.GetRequestStream())
                {
                    requestStream.Write(bytes, 0, bytes.Length);
                }

                using (var httpWebResponse = GetResponse(httpWebRequest))
                {
                    if (httpWebResponse == null)
                    {
                        LoginErrorCode = PicasaLoginError.ConnectionError;
                        return false;
                    }

                    var text3 = ReadResponseBody(httpWebResponse, utf);
                    if (httpWebResponse.StatusCode == HttpStatusCode.OK)
                    {
                        var array = text3.Split(new string[]
                        {
                            "\n"
                        }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var text4 in array)
                        {
                            var array3 = text4.Split(new char[]
                            {
                                '='
                            });
                            if (array3.Length >= 2 && array3[0] == "Auth")
                            {
                                Auth = array3[1];
                                result = true;
                            }
                        }
                    }
                    else if (httpWebResponse.StatusCode == HttpStatusCode.Forbidden)
                    {
                        var array4 = text3.Split(new string[]
                        {
                            "\n"
                        }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var text5 in array4)
                        {
                            var array6 = text5.Split(new char[]
                            {
                                '='
                            });
                            if (array6.Length >= 2 && array6[0] == "Error")
                            {
                                LoginErrorCode = GetLoginError(array6[1]);
                            }
                        }
                    }
                    else
                    {
                        LoginErrorCode = PicasaLoginError.ConnectionError;
                    }
                }
            }
            catch (WebException ex)
            {
                Console.WriteLine("Picasa login failed: " + ex);
                LoginErrorCode = PicasaLoginError.ConnectionError;
            }

            return result;
        }

        // Token: 0x060003EA RID: 1002 RVA: 0x00018430 File Offset: 0x00016630
        public string CreateAlbum(string title, string description, bool isPublic)
        {
            if (Auth.Length == 0)
            {
                throw new Exception(Lang.T("Picasa.NotLoggedIn"));
            }
            var xmlDocument = new XmlDocument();
            var xmlElement = xmlDocument.CreateElement("entry");
            xmlElement.Attributes.Append(xmlDocument.CreateAttribute("xmlns")).Value = "http://www.w3.org/2005/Atom";
            xmlElement.Attributes.Append(xmlDocument.CreateAttribute("xmlns:gphoto")).Value = "http://schemas.google.com/photos/2007";
            xmlDocument.AppendChild(xmlElement);
            var xmlElement2 = xmlDocument.CreateElement("title");
            xmlElement2.Attributes.Append(xmlDocument.CreateAttribute("type")).Value = "text";
            xmlElement2.InnerText = title;
            xmlElement.AppendChild(xmlElement2);
            var xmlElement3 = xmlDocument.CreateElement("summary");
            xmlElement3.Attributes.Append(xmlDocument.CreateAttribute("type")).Value = "text";
            xmlElement3.InnerText = description;
            xmlElement.AppendChild(xmlElement3);
            xmlElement.AppendChild(xmlDocument.CreateElement("gphoto-xmlc-access")).InnerText = (isPublic ? "public" : "private");
            xmlElement.AppendChild(xmlDocument.CreateElement("gphoto-xmlc-commentingEnabled")).InnerText = "false";
            var xmlElement4 = xmlDocument.CreateElement("category");
            xmlElement4.Attributes.Append(xmlDocument.CreateAttribute("scheme")).Value = "http://schemas.google.com/g/2005#kind";
            xmlElement4.Attributes.Append(xmlDocument.CreateAttribute("term")).Value = "http://schemas.google.com/photos/2007#album";
            xmlElement.AppendChild(xmlElement4);
            return CreateAlbum(xmlDocument);
        }

        // Token: 0x060003EB RID: 1003 RVA: 0x000185D0 File Offset: 0x000167D0
        private string CreateAlbum(XmlDocument document)
        {
            var result = "";
            var s = document.InnerXml.Replace("-xmlc-", ":");
            var bytes = Encoding.UTF8.GetBytes(s);
            var httpWebRequest = (HttpWebRequest)WebRequest.Create(Url);
            httpWebRequest.Method = "POST";
            httpWebRequest.ContentType = "application/atom+xml";
            httpWebRequest.Headers.Add(HttpRequestHeader.Authorization, "GoogleLogin auth=" + Auth);
            httpWebRequest.ContentLength = bytes.Length;

            try
            {
                using (var requestStream = httpWebRequest.GetRequestStream())
                {
                    requestStream.Write(bytes, 0, bytes.Length);
                }

                using (var httpWebResponse = GetResponse(httpWebRequest))
                {
                    if (httpWebResponse == null)
                    {
                        return result;
                    }

                    var xml = ReadResponseBody(httpWebResponse, Encoding.UTF8);
                    if (httpWebResponse.StatusCode == HttpStatusCode.Created)
                    {
                        var xmlDocument = new XmlDocument();
                        xmlDocument.LoadXml(xml);
                        result = xmlDocument["entry"]["gphoto:id"].InnerText;
                    }
                }
            }
            catch (WebException ex)
            {
                Console.WriteLine("Picasa album creation failed: " + ex);
            }

            return result;
        }

        // Token: 0x060003EC RID: 1004 RVA: 0x0001872C File Offset: 0x0001692C
        public string ExistAlbum(string albumname)
        {
            if (Auth.Length == 0)
            {
                throw new Exception(Lang.T("Picasa.NotLoggedIn"));
            }
            var httpWebRequest = (HttpWebRequest)WebRequest.Create(AlbumFeedUrl);
            httpWebRequest.Method = "GET";
            httpWebRequest.Headers.Add(HttpRequestHeader.Authorization, "GoogleLogin auth=" + Auth);

            try
            {
                using (var httpWebResponse = GetResponse(httpWebRequest))
                {
                    if (httpWebResponse == null)
                    {
                        return "";
                    }

                    var xml = ReadResponseBody(httpWebResponse, Encoding.UTF8);
                    if (httpWebResponse.StatusCode == HttpStatusCode.OK)
                    {
                        var xmlDocument = new XmlDocument();
                        xmlDocument.LoadXml(xml);
                        var elementsByTagName = xmlDocument["feed"].GetElementsByTagName("entry");
                        foreach (var obj in elementsByTagName)
                        {
                            var xmlElement = (XmlElement)obj;
                            if (xmlElement["title"].InnerText == albumname)
                            {
                                return xmlElement["gphoto:id"].InnerText;
                            }
                        }
                    }
                }
            }
            catch (WebException ex)
            {
                Console.WriteLine("Picasa album lookup failed: " + ex);
            }

            return "";
        }

        // Token: 0x060003ED RID: 1005 RVA: 0x000188B0 File Offset: 0x00016AB0
        public bool UploadPhoto(string album, string imgfile, BackgroundWorker bgw, int progress)
        {
            var result = false;
            var httpWebRequest = (HttpWebRequest)WebRequest.Create(UploadUrl + album);
            httpWebRequest.Method = "POST";
            httpWebRequest.ContentType = "image/jpg";
            httpWebRequest.Headers.Add("Slug: " + Path.GetFileName(imgfile));
            httpWebRequest.Headers.Add(HttpRequestHeader.Authorization, "GoogleLogin auth=" + Auth);

            try
            {
                using (var fileStream = new FileStream(imgfile, FileMode.Open, FileAccess.Read))
                {
                    httpWebRequest.ContentLength = fileStream.Length;
                    using (var requestStream = httpWebRequest.GetRequestStream())
                    {
                        var array = new byte[4096];
                        var num = 100 - progress;
                        var num2 = 0;
                        for (; ; )
                        {
                            var num3 = fileStream.Read(array, 0, array.Length);
                            if (num3 == 0)
                            {
                                break;
                            }
                            requestStream.Write(array, 0, num3);
                            num2 += (int)(num3 / (float)httpWebRequest.ContentLength * num);
                            bgw.ReportProgress(num2);
                        }
                    }
                }

                bgw.ReportProgress(100);
                using (var httpWebResponse = GetResponse(httpWebRequest))
                {
                    if (httpWebResponse == null)
                    {
                        UploadErrorCode = PicasaUploadError.Unknown;
                        return false;
                    }

                    ReadResponseBody(httpWebResponse, Encoding.UTF8);
                    if (httpWebResponse.StatusCode == HttpStatusCode.Created || httpWebResponse.StatusCode == HttpStatusCode.OK)
                    {
                        result = true;
                    }
                    else
                    {
                        var statusCode = httpWebResponse.StatusCode;
                        switch (statusCode)
                        {
                            case HttpStatusCode.BadRequest:
                                UploadErrorCode = PicasaUploadError.BadRequest;
                                return result;
                            case HttpStatusCode.Unauthorized:
                                UploadErrorCode = PicasaUploadError.UnAuthorized;
                                return result;
                            case HttpStatusCode.PaymentRequired:
                                break;
                            case HttpStatusCode.Forbidden:
                                UploadErrorCode = PicasaUploadError.Forbidden;
                                return result;
                            case HttpStatusCode.NotFound:
                                UploadErrorCode = PicasaUploadError.NotFound;
                                return result;
                            default:
                                if (statusCode == HttpStatusCode.Conflict)
                                {
                                    UploadErrorCode = PicasaUploadError.Conflict;
                                    return result;
                                }
                                if (statusCode == HttpStatusCode.InternalServerError)
                                {
                                    UploadErrorCode = PicasaUploadError.InternalServerError;
                                    return result;
                                }
                                break;
                        }
                        UploadErrorCode = PicasaUploadError.Unknown;
                    }
                }
            }
            catch (WebException ex)
            {
                Console.WriteLine("Picasa upload failed: " + ex);
                UploadErrorCode = PicasaUploadError.Unknown;
            }

            return result;
        }

        private static HttpWebResponse GetResponse(HttpWebRequest request)
        {
            try
            {
                return (HttpWebResponse)request.GetResponse();
            }
            catch (WebException ex)
            {
                var response = ex.Response as HttpWebResponse;
                if (response == null)
                {
                    throw;
                }
                Console.WriteLine("Picasa request returned an error response: " + ex);
                return response;
            }
        }

        private static string ReadResponseBody(HttpWebResponse response, Encoding encoding)
        {
            using (var responseStream = response.GetResponseStream())
            {
                if (responseStream == null)
                {
                    return "";
                }

                using (var streamReader = new StreamReader(responseStream, encoding))
                {
                    return streamReader.ReadToEnd();
                }
            }
        }

        private static PicasaLoginError GetLoginError(string error)
        {
            switch (error)
            {
                case "BadAuthentication":
                    return PicasaLoginError.BadAuthentication;
                case "NotVerified":
                    return PicasaLoginError.NotVerified;
                case "TermsNotAgreed":
                    return PicasaLoginError.TermsNotAgreed;
                case "CaptchaRequired":
                    return PicasaLoginError.CaptchaRequired;
                case "AccountDeleted":
                    return PicasaLoginError.AccountDeleted;
                case "AccountDisabled":
                    return PicasaLoginError.AccountDisabled;
                case "ServiceDisabled":
                    return PicasaLoginError.ServiceDisabled;
                case "ServiceUnavailable":
                    return PicasaLoginError.ServiceUnavailable;
                default:
                    return PicasaLoginError.Unknown;
            }
        }

        // Token: 0x04000238 RID: 568
        public PicasaLoginError LoginErrorCode;

        // Token: 0x04000239 RID: 569
        public PicasaUploadError UploadErrorCode;

        // Token: 0x0400023A RID: 570
        private string Auth;

        // Token: 0x0400023B RID: 571
        private string ID;
    }
}
