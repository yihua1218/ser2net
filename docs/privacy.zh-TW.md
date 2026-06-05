# Privacy Policy

除非使用者或安裝/操作此軟體的人明確設定，Ser2Net 不會把資訊傳送到其他 networked systems。

Ser2Net 是可設定的 serial 與 gensio endpoint bridge。它只會開啟或連接 operator 設定的 endpoints，例如 local serial ports、TCP ports 或其他 gensio endpoints。

Windows tray manager 與 service wrapper 在正式安裝時，會把設定與 logs 存放在本機的 `C:\ProgramData\Ser2Net`。portable builds 若以 `--root` 執行，設定會存放在 portable folder 的相對路徑。

Ser2Net 不包含 telemetry、analytics、advertising 或 automatic crash report upload。

使用者需要自行保護放在 Ser2Net configuration files 裡的 credentials、keys、certificates 或 endpoint addresses。
