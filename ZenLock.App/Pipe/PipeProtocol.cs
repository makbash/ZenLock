using System.Security.Principal;

namespace ZenLock.Pipe;

/// <summary>Gate ile resident süreç arasındaki named pipe sözleşmesi.</summary>
public static class PipeProtocol
{
    /// <summary>Kullanıcıya özel pipe adı (aynı makinede başka kullanıcıyla çakışmasın).</summary>
    public static string PipeName
    {
        get
        {
            string sid;
            try { sid = WindowsIdentity.GetCurrent().User?.Value ?? "default"; }
            catch { sid = "default"; }
            return $"ZenLock_{sid}";
        }
    }
}

/// <summary>Gate -> Resident istek.</summary>
public sealed class UnlockRequest
{
    /// <summary>"unlock" | "relock"</summary>
    public string Op { get; set; } = "unlock";

    /// <summary>İlgili exe adı (leaf), ör. "zen.exe".</summary>
    public string Exe { get; set; } = "";

    /// <summary>"unlock" için girilen şifre. "relock" için boş.</summary>
    public string Password { get; set; } = "";
}

/// <summary>Resident -> Gate yanıt.</summary>
public sealed class UnlockResponse
{
    /// <summary>İşlem başarılı mı (şifre doğru / geçit açıldı vb.).</summary>
    public bool Ok { get; set; }

    /// <summary>"badpass" | "nopassword" | "error" | "" (başarı).</summary>
    public string Reason { get; set; } = "";
}
