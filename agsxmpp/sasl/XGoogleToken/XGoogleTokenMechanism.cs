/* * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * *
 * Copyright (c) 2003-2007 by AG-Software 											 *
 * All Rights Reserved.																 *
 * Contact information for AG-Software is available at http://www.ag-software.de	 *
 *																					 *
 * Licence:																			 *
 * The agsXMPP SDK is released under a dual licence									 *
 * agsXMPP can be used under either of two licences									 *
 * 																					 *
 * A commercial licence which is probably the most appropriate for commercial 		 *
 * corporate use and closed source projects. 										 *
 *																					 *
 * The GNU Public License (GPL) is probably most appropriate for inclusion in		 *
 * other open source projects.														 *
 *																					 *
 * See README.html for details.														 *
 *																					 *
 * For general enquiries visit our website at:										 *
 * http://www.ag-software.de														 *
 * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * */ 

using System;
using System.IO;
using System.Text;
using System.Net;

using agsXMPP;
using agsXMPP.Xml.Dom;
using agsXMPP.protocol.sasl;

namespace agsXMPP.sasl.XGoogleToken
{
	/// <summary>
	/// X-GOOGLE-TOKEN Authentication
	/// </summary>
	public class XGoogleTokenMechanism : Mechanism
	{

        /*
        
        see Google API documentation at:
        http://code.google.com/apis/accounts/AuthForInstalledApps.html
        http://code.google.com/apis/accounts/AuthForWebApps.html
        
        */

        private         string                  _Sid                    = null;
        private         string                  _Lsid                   = null;
        private         string                  _Base64Token            = null;

        private const   string                  METHOD                  = "POST";
        private const   string                  CONTENT_TYPE            = "application/x-www-form-urlencoded";
        private const   string                  URL_ISSUE_AUTH_TOKEN    = "https://www.google.com/accounts/IssueAuthToken";
        private const   string                  URL_CLIENT_AUTH         = "https://www.google.com/accounts/ClientAuth";        
        

        public XGoogleTokenMechanism()
		{			
		}

		public override void Init(XmppClientConnection con)
		{
            base.XmppClientConnection = con;			

            DoClientAuth();
            
		}

		public override void Parse(Node e)
		{
			// not needed here in X-GOOGLE-TOKEN mechanism
		}

        private void DoSaslAuth()
        {
            // <auth xmlns=�urn:ietf:params:xml:ns:xmpp-sasl� mechanism=�X-GOOGLE-TOKEN�>Base 64 Token goes here</auth>            
            Auth auth = new Auth(MechanismType.X_GOOGLE_TOKEN, _Base64Token);           
            base.XmppClientConnection.Send(auth);
        }
       
        private void DoClientAuth()
        {
            HttpWebRequest request = (HttpWebRequest) HttpWebRequest.Create(URL_CLIENT_AUTH);
                        
            request.Method          = METHOD;
            request.ContentType     = CONTENT_TYPE;
            
#if CF || CF_2
            //required for bug workaround
            request.AllowWriteStreamBuffering = true; 
#endif

            request.BeginGetRequestStream(new AsyncCallback(OnGetClientAuthRequestStream), request);
        }

        private void OnGetClientAuthRequestStream(IAsyncResult result)
        {
            WebRequest request = (System.Net.WebRequest)result.AsyncState;
            Stream outputStream = request.EndGetRequestStream(result);

            string data = null;
            data += "Email=" + base.XmppClientConnection.MyJID.Bare;
            data += "&Passwd=" + base.Password;
            data += "&PersistentCookie=false";
            data += "&source=googletalk";
            
            byte[] bytes = Encoding.UTF8.GetBytes(data);
            outputStream.Write(bytes, 0, bytes.Length);
            outputStream.Close();

            request.BeginGetResponse(new AsyncCallback(OnGetClientAuthResponse), request);
        }

        private void OnGetClientAuthResponse(IAsyncResult result)
        {
            WebRequest request = (WebRequest)result.AsyncState;            
            HttpWebResponse response = (HttpWebResponse)request.EndGetResponse(result);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                Stream dataStream = response.GetResponseStream();

                ParseClientAuthResponse(dataStream);
                                
                dataStream.Close();
                response.Close();

                DoIssueAuthToken();
            }
            else
                base.XmppClientConnection.Close();
        }              

        private void ParseClientAuthResponse(Stream responseStream)
        {            
            StreamReader reader = new StreamReader(responseStream);
            
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.StartsWith("SID="))
                    _Sid = line.Substring(4);
                else if(line.StartsWith("LSID="))
                    _Lsid = line.Substring(5);
            }

            reader.Close();            
        }

        private void DoIssueAuthToken()
        {
            HttpWebRequest request = (HttpWebRequest) HttpWebRequest.Create(URL_ISSUE_AUTH_TOKEN);
                        
            request.Method = METHOD;
            request.ContentType = CONTENT_TYPE;
#if CF || CF_2
            //required for bug workaround
            request.AllowWriteStreamBuffering = true;
#endif
            request.BeginGetRequestStream(new AsyncCallback(this.OnGetIssueAuthTokenRequestStream), request);
        }

        private void OnGetIssueAuthTokenRequestStream(IAsyncResult result)
        {
            WebRequest request = (System.Net.WebRequest)result.AsyncState;
            Stream outputStream = request.EndGetRequestStream(result);

            string data = null;                        
            data += "SID=" + _Sid;
            data += "&LSID=" + _Lsid;
            data += "&service=mail&Session=true";            
            
            byte[] bytes = Encoding.UTF8.GetBytes(data);
            outputStream.Write(bytes, 0, bytes.Length);
            outputStream.Close();

            request.BeginGetResponse(new AsyncCallback(OnGetIssueAuthTokenResponse), request);
        }

        private void OnGetIssueAuthTokenResponse(IAsyncResult result)
        {
            WebRequest request = (WebRequest)result.AsyncState;
            HttpWebResponse response = (HttpWebResponse) request.EndGetResponse(result);
            
            if (response.StatusCode == HttpStatusCode.OK)
            {                
                Stream dataStream = response.GetResponseStream();
                ParseIssueAuthTokenResponse(dataStream);
                             
                dataStream.Close();
                response.Close();

                DoSaslAuth();
            }
            else
                base.XmppClientConnection.Close();
        }

        /// <summary>
        /// Parse the response and build the token
        /// </summary>
        /// <param name="responseStream"></param>
        private void ParseIssueAuthTokenResponse(Stream responseStream)
        {           
            StreamReader reader = new StreamReader(responseStream);
            
            string line;
            while ((line = reader.ReadLine()) != null)
            {                
                string temp = "\0" + base.XmppClientConnection.MyJID.Bare + "\0" + line;
                byte[] b = Encoding.UTF8.GetBytes(temp);
                _Base64Token = Convert.ToBase64String(b, 0, b.Length);                
            }
            reader.Close();            
        }        
	}
}