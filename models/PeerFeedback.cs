public class PeerFeedback
{
    public int Id { get; set; }
    public string TargetEmployeeId { get; set; }
    public string AnonymousPeerId { get; set; } = "PEER-" + Guid.NewGuid().ToString().Substring(0,4);
    public double Score { get; set; }
    public string Comment { get; set; } = string.Empty;
    public DateTime DateSubmitted { get; set; } = DateTime.Now;
}