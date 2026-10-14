namespace DocumentFlow.Domain;

public sealed class WebhookEndpoint
{
    private WebhookEndpoint() { }
    public WebhookEndpoint(Guid id, string url, string eventTypes, string encryptedSecret, DateTime createdAtUtc)
    {
        Id = id; Url = url; EventTypes = eventTypes; EncryptedSecret = encryptedSecret;
        CreatedAtUtc = DateTime.SpecifyKind(createdAtUtc, DateTimeKind.Utc); IsActive = true;
    }
    public Guid Id { get; private set; }
    public string Url { get; private set; } = string.Empty;
    public string EventTypes { get; private set; } = string.Empty;
    public string EncryptedSecret { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public void SetActive(bool active) => IsActive = active;
    public void Delete() { IsActive = false; IsDeleted = true; }
    public void RotateSecret(string encryptedSecret) => EncryptedSecret = encryptedSecret;
}
