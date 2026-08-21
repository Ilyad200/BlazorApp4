using System.Net;
using System.Text;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;

namespace BlazorApp4.Services
{
    // Диффинг реализован через библиотеку DiffPlex (NuGet-пакет "DiffPlex",
    // https://github.com/mmanela/diffplex) — построчный + словесный diff внутри
    // изменённых строк. Нужно установить пакет в проект:
    //   dotnet add package DiffPlex
    // Это ЛЕКСИЧЕСКИЙ (текстовый) diff, не семантический — см. комментарий внизу файла.
    public static class TextDiffService
    {
        public record DiffResult(SideBySideDiffModel Model, double Similarity);

        public static DiffResult Compare(string? a, string? b)
        {
            a ??= "";
            b ??= "";

            // ignoreWhiteSpace: false — нам важно видеть форматирование ответа как есть.
            var model = SideBySideDiffBuilder.Diff(a, b, ignoreWhiteSpace: false, ignoreCase: false);
            double similarity = ComputeSimilarity(model);
            return new DiffResult(model, similarity);
        }

        // Рендерит HTML для "своей" стороны — той, что была передана первым
        // параметром (a) в Compare(). Общий текст — как есть, уникальный для этой
        // стороны текст — подсвечен. Строки/слова, существующие только у
        // визави, здесь не показываются (это не часть данного сообщения).
        public static string RenderOwnSideHtml(DiffResult diff) => RenderPaneHtml(diff.Model.OldText);

        private static string RenderPaneHtml(DiffPaneModel pane)
        {
            var sb = new StringBuilder();
            foreach (var line in pane.Lines)
            {
                switch (line.Type)
                {
                    case ChangeType.Imaginary:
                        // "Дырка", соответствующая строке, добавленной/удалённой у визави —
                        // на этой стороне ей ничего не соответствует.
                        continue;

                    case ChangeType.Unchanged:
                        sb.Append(WebUtility.HtmlEncode(line.Text ?? "")).Append("<br>");
                        break;

                    case ChangeType.Modified when line.SubPieces != null:
                        foreach (var sub in line.SubPieces)
                        {
                            var encoded = WebUtility.HtmlEncode(sub.Text ?? "");
                            sb.Append(sub.Type == ChangeType.Unchanged
                                ? encoded
                                : $"<mark class=\"diff-unique\">{encoded}</mark>");
                        }
                        sb.Append("<br>");
                        break;

                    default: // Deleted / Inserted — целая строка уникальна для этой стороны
                        sb.Append("<mark class=\"diff-unique\">")
                          .Append(WebUtility.HtmlEncode(line.Text ?? ""))
                          .Append("</mark><br>");
                        break;
                }
            }
            return sb.ToString();
        }

        // Dice-коэффициент по словам: 2*совпавшие / (словA + словB).
        private static double ComputeSimilarity(SideBySideDiffModel model)
        {
            var (totalOld, matchedOld) = CountWords(model.OldText);
            var (totalNew, matchedNew) = CountWords(model.NewText);

            int totalWords = totalOld + totalNew;
            if (totalWords == 0) return 1.0;

            // matchedOld и matchedNew в теории должны совпадать (это одни и те же
            // общие слова, посчитанные с обеих сторон) — усредняем на случай
            // небольших расхождений в подсчёте отдельных Modified-строк.
            double matched = (matchedOld + matchedNew) / 2.0;
            return (2.0 * matched) / totalWords;
        }

        private static (int total, int matched) CountWords(DiffPaneModel pane)
        {
            int total = 0, matched = 0;
            foreach (var line in pane.Lines)
            {
                switch (line.Type)
                {
                    case ChangeType.Imaginary:
                        continue;

                    case ChangeType.Unchanged:
                        var wc = WordCount(line.Text);
                        total += wc;
                        matched += wc;
                        break;

                    case ChangeType.Modified when line.SubPieces != null:
                        foreach (var sub in line.SubPieces)
                        {
                            var swc = WordCount(sub.Text);
                            total += swc;
                            if (sub.Type == ChangeType.Unchanged) matched += swc;
                        }
                        break;

                    default: // Deleted / Inserted — целая строка уникальна для этой стороны
                        total += WordCount(line.Text);
                        break;
                }
            }
            return (total, matched);
        }

        private static int WordCount(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            return text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        }
    }

    // ИДЕЯ НА БУДУЩЕЕ (не реализовано): настоящая смысловая оценка через LLM-судью.
    // Отдельным вызовом AiService спросить модель:
    //   "Оцени по шкале 0-100, насколько два ответа похожи по смыслу
    //    (не по формулировке). Ответь только числом.\n\nОтвет A: ...\nОтвет B: ..."
    // Плюсы: реально ловит перефразирование. Минусы: +1 сетевой запрос на каждую
    // пару сообщений -> задержка и расход токенов/лимитов бесплатной модели,
    // и оценка не детерминирована (можно закэшировать результат в БД, привязав
    // к паре VersionId, чтобы не пересчитывать каждый рендер).
}
