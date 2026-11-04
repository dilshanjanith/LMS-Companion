"""Fetch captions for one validated YouTube video ID; output bounded JSON only."""
import json
import re
import sys
import urllib.parse
import urllib.request


def result(status, content=None, detail=None):
    print(json.dumps({"status": status, "content": content, "detail": detail}, ensure_ascii=False))


def main():
    video_id = sys.argv[1] if len(sys.argv) == 2 else ""
    if not re.fullmatch(r"[A-Za-z0-9_-]{11}", video_id):
        result("transcriptRequired", detail="The linked YouTube video ID is invalid.")
        return
    try:
        import yt_dlp

        with yt_dlp.YoutubeDL({"quiet": True, "no_warnings": True, "skip_download": True,
                              "noplaylist": True, "extract_flat": False}) as youtube:
            info = youtube.extract_info(f"https://www.youtube.com/watch?v={video_id}", download=False)
        tracks = info.get("subtitles", {}).get("en", []) or info.get("automatic_captions", {}).get("en", [])
        track = next((item for item in tracks if item.get("ext") == "json3"), None)
        if track is None:
            result("transcriptRequired", detail="This video has no accessible English caption track.")
            return
        url = urllib.parse.urlparse(track["url"])
        if url.scheme != "https" or url.hostname != "www.youtube.com" or url.path != "/api/timedtext":
            result("transcriptRequired", detail="YouTube returned an unsupported caption location.")
            return
        request = urllib.request.Request(track["url"], headers={"User-Agent": "Mozilla/5.0"})
        with urllib.request.urlopen(request, timeout=15) as response:
            if response.url != track["url"]:
                result("transcriptRequired", detail="YouTube redirected the caption request.")
                return
            raw = response.read(2_000_001)
        if len(raw) > 2_000_000:
            result("tooLarge", detail="The caption track exceeds the retrieval limit.")
            return
        events = json.loads(raw).get("events", [])
        lines = []
        for event in events:
            phrase = "".join(segment.get("utf8", "") for segment in event.get("segs", []))
            phrase = " ".join(phrase.split())
            if phrase:
                elapsed = int(event.get("tStartMs", 0)) // 1000
                lines.append(f"[{elapsed // 60:02d}:{elapsed % 60:02d}] {phrase}")
        transcript = "\n".join(lines)
        if not transcript:
            result("transcriptRequired", detail="YouTube returned an empty caption track.")
        else:
            result("available", transcript[:1_000_000],
                   "YouTube captions, which may be automatically generated and contain errors.")
    except Exception:
        result("transcriptRequired", detail="The YouTube caption track could not be retrieved.")


if __name__ == "__main__":
    main()
