using System.Text;
using DropPod.Client.Web;
using FauFau.Net.Web;

namespace DropPod.Tests.Web;

[TestClass]
public sealed class Red5CredentialsTests
{
    private static readonly Red5Credentials Account = new("Pod001@DropPod.local", "hunter22");

    [TestMethod]
    public void SignatureVerifiesWithTheServerCheck()
    {
        var uri = new Uri("https://localhost:5001/clientapi/api/v1/oracle/ticket");
        var body = Encoding.UTF8.GetBytes("""{"ver":2,"build":"prod-1962"}""");

        var header = Account.CreateSignature(uri, body, 0x7f3cb864ab3492fe);

        var result = Red5Sig.Verify(Account.Secret, header, body, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5), "localhost:5001", "/clientapi/api/v1/oracle/ticket");
        Assert.AreEqual(Red5Sig.VerifyResult.Valid, result);
        Assert.AreEqual(0x7f3cb864ab3492feul, Red5Sig.ParseString(header).Cid);
    }

    [TestMethod]
    public void SignatureCoversTheBody()
    {
        var uri = new Uri("https://localhost:5001/clientapi/api/v2/accounts/login");
        var header = Account.CreateSignature(uri, []);

        var result = Red5Sig.Verify(Account.Secret, header, "{}"u8, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5));
        Assert.AreEqual(Red5Sig.VerifyResult.BodyMismatch, result);
    }

    [TestMethod]
    public void UidIgnoresTheCaseOfTheEmail()
    {
        Assert.AreEqual(Account.Uid, new Red5Credentials("pod001@droppod.local", "hunter22").Uid);
        Assert.AreEqual(Account.Secret, new Red5Credentials("pod001@droppod.local", "hunter22").Secret);
    }
}
