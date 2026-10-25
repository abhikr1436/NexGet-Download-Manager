@echo off
rem Sign one file with your code-signing certificate (e.g. Certum via SimplySign Desktop, or any cert in your Windows store).
rem Set NEXGET_CERT to the certificate's subject name (as shown in certmgr), e.g. set NEXGET_CERT=Open Source Developer, Your Name
if "%NEXGET_CERT%"=="" (echo Set NEXGET_CERT first & exit /b 1)
signtool sign /n "%NEXGET_CERT%" /fd sha256 /tr http://time.certum.pl /td sha256 /d "NexGet Download Manager" %1
