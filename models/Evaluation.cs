public class Evaluation
{
    public int Id { get; set; }
    public int TargetEmployeeId { get; set; } 
    public int EvaluatorId { get; set; }  
    public double Score { get; set; }      
    public string Comments { get; set; } = string.Empty;
    public DateTime DateSubmitted { get; set; } = DateTime.Now;
}