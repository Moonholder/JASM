namespace GIMI_ModManager.WinUI.Helpers;

public static class GameBananaHtmlHelper
{
    public static string WrapHtml(string htmlContent)
    {
        const string css = @"
            body {
                background: transparent;
                font-family: 'Segoe UI Variable', 'Segoe UI', -apple-system, sans-serif;
                font-size: 13px;
                line-height: 1.6;
                margin: 0;
                padding: 12px;
                word-wrap: break-word;
            }
            .update-entry {
                margin-bottom: 24px;
                padding-bottom: 24px;
                border-bottom: 1px solid rgba(128,128,128,0.2);
            }
            .update-entry:last-child { border-bottom: none; }
            .update-header {
                display: flex;
                align-items: baseline;
                margin-bottom: 12px;
            }
            .icon { font-size: 11px; margin-right: 6px; color: #888; }
            .title { font-size: 15px; font-weight: 600; color: #e3c454; }
            .version { font-size: 12px; color: #d15a5a; margin-left: 8px; font-weight: 300; }
            .date-right { margin-left: auto; font-size: 13px; color: #888; }
            .changelog-items { margin-bottom: 12px; display: flex; flex-direction: column; gap: 6px; }
            .cl-item { display: flex; font-size: 12px; line-height: 1.4; align-items: flex-start; }
            .tag {
                display: inline-block; padding: 1px 6px; border-radius: 4px; font-size: 11px;
                font-weight: 500; margin-right: 12px; white-space: nowrap; font-family: 'Segoe UI', sans-serif;
            }
            .cl-text { flex: 1; font-family: monospace; font-size: 13px;}
            .update-text { margin-top: 14px; font-size: 14px; }
            .update-files { margin-top: 20px; }
            .files-label { font-size: 11px; font-weight: 600; color: #888; margin-bottom: 6px; }
            .file-list { list-style-type: none; padding: 0; margin: 0; }
            .file-list li {
                position: relative; padding-left: 16px; font-family: 'Consolas', monospace;
                font-size: 13px; color: #e3c454; margin-bottom: 4px;
            }
            .file-list li::before { content: '•'; position: absolute; left: 4px; color: #888; }

            ::-webkit-scrollbar { width: 14px; height: 14px; }
            ::-webkit-scrollbar-track { background: transparent; }
            ::-webkit-scrollbar-thumb {
                background-color: rgba(128, 128, 128, 0.4);
                background-clip: padding-box; border: 4px solid rgba(0, 0, 0, 0); border-radius: 8px;
            }
            ::-webkit-scrollbar-thumb:hover { background-color: rgba(128, 128, 128, 0.6); }

            @media (prefers-color-scheme: dark) {
                body { color: #d0d0d0; }
                a { color: #5bc2e7; }
                .tag-adjustment { background-color: rgba(60, 100, 60, 0.4); border: 1px solid rgba(80, 160, 80, 0.5); color: #8FBC8F; }
                .tag-addition { background-color: rgba(60, 80, 120, 0.4); border: 1px solid rgba(80, 120, 180, 0.5); color: #8FAACC; }
                .tag-bugfix { background-color: rgba(120, 60, 60, 0.4); border: 1px solid rgba(180, 80, 80, 0.5); color: #CC8F8F; }
                .tag-improvement { background-color: rgba(120, 60, 100, 0.4); border: 1px solid rgba(180, 80, 150, 0.5); color: #CC8FCC; }
                .tag-overhaul { background-color: rgba(120, 80, 40, 0.4); border: 1px solid rgba(180, 120, 60, 0.5); color: #CCAA8F; }
                .tag-optimization { background-color: rgba(80, 60, 120, 0.4); border: 1px solid rgba(120, 80, 180, 0.5); color: #AA8FCC; }
                .tag-removal { background-color: rgba(180, 60, 60, 0.4); border: 1px solid rgba(220, 80, 80, 0.5); color: #E88F8F; }
                .tag-default { background-color: rgba(128, 128, 128, 0.4); border: 1px solid rgba(160, 160, 160, 0.5); color: #CCCCCC; }
                .cl-text { color: #9cbbd3; }
            }
            @media (prefers-color-scheme: light) {
                body { color: #1a1a1a; }
                a { color: #005fb8; }
                .title { color: #a18a3a; }
                .file-list li { color: #a18a3a; }
                .icon { color: #666; }
                .tag-adjustment { background-color: rgba(60, 100, 60, 0.1); border: 1px solid rgba(80, 160, 80, 0.3); color: #4F7F4F; }
                .tag-addition { background-color: rgba(60, 80, 120, 0.1); border: 1px solid rgba(80, 120, 180, 0.3); color: #4F6F9C; }
                .tag-bugfix { background-color: rgba(120, 60, 60, 0.1); border: 1px solid rgba(180, 80, 80, 0.3); color: #9C4F4F; }
                .tag-improvement { background-color: rgba(120, 60, 100, 0.1); border: 1px solid rgba(180, 80, 150, 0.3); color: #9C4F9C; }
                .tag-overhaul { background-color: rgba(120, 80, 40, 0.1); border: 1px solid rgba(180, 120, 60, 0.3); color: #9C7A4F; }
                .tag-optimization { background-color: rgba(80, 60, 120, 0.1); border: 1px solid rgba(120, 80, 180, 0.3); color: #7A4F9C; }
                .tag-removal { background-color: rgba(180, 60, 60, 0.1); border: 1px solid rgba(220, 80, 80, 0.3); color: #AF4F4F; }
                .tag-default { background-color: rgba(128, 128, 128, 0.1); border: 1px solid rgba(160, 160, 160, 0.3); color: #666666; }
                .cl-text { color: #3b5a73; }
            }
            img { max-width: 100%; height: auto; border-radius: 4px; margin: 4px 0; }
            a { text-decoration: none; }
            a:hover { text-decoration: underline; }
            ul, ol { padding-left: 20px; }
            blockquote { margin: 8px 0; padding: 4px 12px; border-left: 3px solid #555; color: #aaa; }
            @media (prefers-color-scheme: light) { blockquote { border-left-color: #ccc; color: #444; } }";

        return $"<!DOCTYPE html><html><head><meta charset=\"utf-8\"/><style>{css}</style></head><body>{htmlContent}</body></html>";
    }
}