# VolumeRasaApi
.NET Core WebAPI to integrate Rasa NLP with QBox&trade;

Steps: 

- Check port 80 is open on VM, otherwise open it (IN and OUT bound) 
- login 
- install dotnet core for Linux 
  - wget https://packages.microsoft.com/config/ubuntu/18.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb 
  - sudo dpkg -i packages-microsoft-prod.deb 
  - sudo apt-get update; sudo apt-get install -y apt-transport-https && sudo apt-get update && sudo apt-get install -y aspnetcore-runtime-3.1 

- install Apache2 
  - sudo apt-get install -y apache2 

- configure Apache  
  (a similar configuration can be used with Nginx if preferred) 
  Configuration files for Apache are located within the /etc/apache2/sites-available/ directory. Create a configuration file, named “volume-rasa-api.conf”, for the app: 

```
<VirtualHost *:*> 
    RequestHeader set "X-Forwarded-Proto" expr=%{REQUEST_SCHEME} 
</VirtualHost> 

<VirtualHost *:80> 
    ProxyPreserveHost On 
    ProxyPass / http://127.0.0.1:5000/ 
    ProxyPassReverse / http://127.0.0.1:5000/ 
    ServerName VolumeRasaApi.com 
    ErrorLog ${APACHE_LOG_DIR}/VolumeRasaApi-error.log 
    CustomLog ${APACHE_LOG_DIR}/VolumeRasaApi-access.log common 
</VirtualHost> 
```
 
- Enable the new site 
    a2ensite volume-rasa-api.conf 

- Disable the default site 
    a2dissite 000-default.conf 
 
- Enable additional modules: headers and proxy 
    a2enmod headers 
    a2enmod proxy 
    a2enmod proxy_http 
    a2enmod ssl 
 
- Reload apache 
    systemctl restart apache2 
    
- Extract "Volume_QBox_RasaApi_X-Y-Z.zip" into a new folder (i.e. "volume-rasa-api") inside "/var/www"

- Edit the extracted "appsettings.json" file 
```
{
  "AppSettings": {
    "Token": "[auth token]",
    "LogDir": "[folder path for logs]",
    "PythonPath": "[folder path for custom python modules]",
    "LogLevel": 1,
    "PredictionTimeout": 10,
    "ProcessTimeout": 30,
    "DefaultRasaVersion": "1.10.14"
  },
  "Rasa": [
    {
      "Version": "[version of Rasa installed]",
      "RasaDir": "[folder path for Rasa binaries, including virtual environment/bin]",
      "ModelDir": "[folder path for Rasa models]"
    },
    {
      "Version": "[version of Rasa installed]",
      "RasaDir": "[folder path for Rasa binaries, including virtual environment/bin]",
      "ModelDir": "[folder path for Rasa models]"
    },
    ...
  ],
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
      "Microsoft.Hosting.Lifetime": "Information",
      "Microsoft.EntityFrameworkCore.Database.Command": "Warning",
      "Volume": "Debug"
    }
  },
  "AllowedHosts": "*",
  "Kestrel": {
    "Endpoints": {
      "Http": {
        "Url": "http://127.0.0.1:5000"
      }
    }
  }
}
```  
**_Notes:_**

"Token" is a custom created token to secure API requests

"LogDir" is the folder path for Rasa service logs

"PythonPath" is the folder path for custom python modules (to be used by Rasa NLP)

For every Rasa version:

  "Version" is the version of Rasa installed (to be shown on Qbox)
  
  "RasaDir" is the folder path for Rasa binaries, including virtual environment/bin
  
  "ModelDir" is the folder path where to store Rasa models
  

- Use systemd and create a service file  
  systemd is an init system that provides many powerful features for starting, stopping, and managing processes. 

  - sudo nano /etc/systemd/system/kestrel-volume-rasa-api.service 
  - Copy the following content

``` 
[Unit] 
Description=.NET Core Web API for Rasa 

[Service] 
WorkingDirectory=/var/www/[api folder] 
ExecStart=/usr/bin/dotnet /var/www/[api folder]/RasaAPI.dll 
Restart=always 

# Restart service after 10 seconds if the dotnet service crashes: 
RestartSec=10 
KillSignal=SIGINT 
SyslogIdentifier=volume-rasa-api 
User=www-data 
Environment=ASPNETCORE_ENVIRONMENT=Production  

[Install] 
WantedBy=multi-user.target 
```
  - replace "[api folder]" witht eh folder name where you extracted the zip archive 
 
- Enable KESTREL service 
```
systemctl enable kestrel-volume-rasa-api.service 
systemctl restart kestrel-volume-rasa-api.service 
systemctl status kestrel-volume-rasa-api.service 
```
  to check if there is any error on Kestrel service 
 
--------- REFERENCES ------------------ 

Host ASP.NET Core api on Linux 

https://docs.microsoft.com/en-us/aspnet/core/host-and-deploy/linux-apache?view=aspnetcore-3.1  

.NET Core on Ubuntu 

https://docs.microsoft.com/en-us/dotnet/core/install/linux-ubuntu#1804- 

 

 
