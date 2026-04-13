java -Xmx2G -Djdk.httpclient.allowRestrictedHeaders=true --add-opens java.base/sun.net.www.protocol.https=ALL-UNNAMED -jar otp-shaded-2.9.0.jar --build --serve .
pause