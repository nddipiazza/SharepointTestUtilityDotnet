using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security;
using System.Text.RegularExpressions;
using CommandLine;
using Microsoft.SharePoint.Client;
using Newtonsoft.Json;

namespace SharepointTestUtility {

  // Commandline options
  class CmdOptions {
    [Option('d', "Domain", Required = true, HelpText = "Domain of sharepoint user")]
    public string Domain { get; set; }

    [Option('u', "Username", Required = true, HelpText = "Sharepoint Username. Include domain\\username if you are on prem")]
    public string Username { get; set; }

    [Option('p', "Password", Required = true, HelpText = "Sharepoint Password")]
    public string Password { get; set; }

    [Option('w', "WebApplicationUrl", Required = true, HelpText = "Sharepoint Web Application Url")]
    public string WebApplicationUrl { get; set; }

    [Option('a', "ActionFile", Required = true, HelpText = "Json file describing what to do")]
    public string ActionFile { get; set; }

  }

  public class Util {
    public static string addSlashToUrlIfNeeded(string siteUrl) {
      string res = siteUrl;
      if (!res.EndsWith("/", StringComparison.CurrentCulture)) {
        res += "/";
      }
      return res;
    }
    public static string getBaseUrl(string siteUrl) {
      return new Uri(siteUrl).Scheme + "://" + new Uri(siteUrl).Host;
    }

    public static int getBaseUrlPort(string siteUrl) {
      return new Uri(siteUrl).Port;
    }

    public static string getBaseUrlHost(string siteUrl) {
      return new Uri(siteUrl).Host;
    }
    public static void deleteDirectory(string targetDir) {
      string[] files = Directory.GetFiles(targetDir);
      string[] dirs = Directory.GetDirectories(targetDir);

      foreach (string file in files) {
        System.IO.File.SetAttributes(file, FileAttributes.Normal);
        System.IO.File.Delete(file);
      }

      foreach (string dir in dirs) {
        deleteDirectory(dir);
      }

      Directory.Delete(targetDir, false);
    }

    public static bool isSharepointOnline(string url) {
      Regex rx = new Regex("https://[-a-zA-Z0-9]+\\.sharepoint\\.com",
          RegexOptions.Compiled | RegexOptions.IgnoreCase);

      return rx.IsMatch(url);
    }
  }

  public class Auth {
    public CredentialCache credentialsCache;
    public SharePointOnlineCredentials sharepointOnlineCredentials;
    public Auth(string rootSite,
                bool isSharepointOnline,
                string domain,
                string username,
                string password,
                string authScheme) {
      if (!isSharepointOnline) {
        NetworkCredential networkCredential;
        if (password == null && username != null) {
          Console.WriteLine("Please enter password for {0}", username);
          networkCredential = new NetworkCredential(username, GetPassword(), domain);
        } else if (username != null) {
          networkCredential = new NetworkCredential(username, password, domain);
        } else {
          networkCredential = CredentialCache.DefaultNetworkCredentials;
        }
        credentialsCache = new CredentialCache();
        credentialsCache.Add(new Uri(rootSite), authScheme, networkCredential);
        CredentialCache credentialCache = new CredentialCache { { Util.getBaseUrlHost(rootSite), Util.getBaseUrlPort(rootSite), authScheme, networkCredential } };
      } else {
        SecureString securePassword = new SecureString();
        foreach (char c in password) {
          securePassword.AppendChar(c);
        }
        sharepointOnlineCredentials = new SharePointOnlineCredentials(username, securePassword);
      }

    }
    SecureString GetPassword() {
      var pwd = new SecureString();
      while (true) {
        ConsoleKeyInfo i = Console.ReadKey(true);
        if (i.Key == ConsoleKey.Enter) {
          break;
        }
        if (i.Key == ConsoleKey.Backspace) {
          if (pwd.Length > 0) {
            pwd.RemoveAt(pwd.Length - 1);
            Console.Write("\b \b");
          }
        } else {
          pwd.AppendChar(i.KeyChar);
          Console.Write("*");
        }
      }
      return pwd;
    }
  }

  class Program {

    Auth auth;
    List<Dictionary<string, object>> actions;

    static SecureString GetSecureString(string input) {
      if (string.IsNullOrEmpty(input))
        throw new ArgumentException("Input string is empty and cannot be made into a SecureString", "input");

      var secureString = new SecureString();
      foreach (char c in input.ToCharArray())
        secureString.AppendChar(c);

      return secureString;
    }

    public ClientContext getClientContext(string site) {
      ClientContext clientContext = new ClientContext(site);
      clientContext.RequestTimeout = -1;
      if (auth.credentialsCache != null) {
        clientContext.Credentials = auth.credentialsCache;
      } else if (auth.sharepointOnlineCredentials != null) {
        clientContext.Credentials = auth.sharepointOnlineCredentials;
      }
      return clientContext;
    }

    public Program(CmdOptions options) {

      string json = System.IO.File.ReadAllText(options.ActionFile);
      actions = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(json);

      auth = new Auth(options.WebApplicationUrl, Util.isSharepointOnline(options.WebApplicationUrl), options.Domain, options.Username, options.Password, "NTLM");

    }

    //Overloaded main, called with CmdOptions from main(string[])
    static void Main(CmdOptions options) {
      Program program = new Program(options);
      program.exec();
    }

    void exec() {
      foreach (Dictionary<string, object> action in actions) {
        string actionType = (string)action["Type"];
        if (actionType.Equals("createSite")) {
          WebCreationInformation webCreationInformation = new WebCreationInformation();
          webCreationInformation.WebTemplate = "STS#0";
          webCreationInformation.Description = (string)action["Description"];
          webCreationInformation.Title = (string)action["Title"];
          webCreationInformation.Url = (string)action["Url"];
          webCreationInformation.UseSamePermissionsAsParentSite = (bool)action["UseSamePermissionsAsParentSite"];
          using (ClientContext clientContext = getClientContext((string)action["ParentSiteUrl"])) {
            var site = clientContext.Web.Webs.Add(webCreationInformation);
            clientContext.Load(site);
            clientContext.ExecuteQuery();

          }
        }
      }
    }

    static void Main(string[] args) {
      var cmdOptions = Parser.Default.ParseArguments<CmdOptions>(args);
      cmdOptions.WithParsed(
          options => {
            Main(options);
          });
    }
  }
}
