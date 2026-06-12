using System.IO.Pipes;
using System.Security.AccessControl;
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

    /// <summary>
    /// Resident'ın (elevated/high IL) sunucu pipe'ını açık DACL ile oluşturur.
    /// Aksi halde elevated süreçin varsayılan güvenlik tanımı, normal IL'deki gate'in
    /// (aynı kullanıcı) bağlanmasını engelliyor — §2 #4. AuthenticatedUsers'a ReadWrite +
    /// CreateNewInstance (sunucunun ek örnek açabilmesi için) verilir.
    /// </summary>
    public static NamedPipeServerStream CreateServerStream()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
            AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            PipeName, PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous,
            inBufferSize: 0, outBufferSize: 0,
            pipeSecurity: security);
    }
}

/// <summary>Gate -> Resident istek.</summary>
public sealed class UnlockRequest
{
    /// <summary>"check" | "unlock" | "relock"</summary>
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

    /// <summary>"badpass" | "nopassword" | "panic" | "error" | "" (başarı).</summary>
    public string Reason { get; set; } = "";
}
