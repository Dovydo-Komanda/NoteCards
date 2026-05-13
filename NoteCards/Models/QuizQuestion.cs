namespace NoteCards.Models;

public enum QuizQuestionType
{
    SingleChoice = 0,
    MultipleChoice = 1,
    TrueFalse = 2
}

public sealed class QuizQuestion
{
    public QuizQuestionType Type { get; set; } = QuizQuestionType.SingleChoice;
    public string Question { get; set; } = string.Empty;
    public List<QuizOption> Options { get; set; } = new();
    public string Explanation { get; set; } = string.Empty;
    public string Hint { get; set; } = string.Empty;  
    public int SetIndex { get; set; } = 1;
}
