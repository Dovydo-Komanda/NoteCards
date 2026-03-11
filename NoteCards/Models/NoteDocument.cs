using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NoteCards.Models;

public class NoteDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string FontFamily { get; set; } = "Segoe UI";
    public double FontSize { get; set; } = 14;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}