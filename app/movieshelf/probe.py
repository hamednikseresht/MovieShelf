"""Read what is really inside a video file with ffprobe (part of ffmpeg): length, picture size,
audio languages and subtitles. Optional: without ffprobe the app works from the file names."""
import glob
import json
import os
import shutil
import subprocess

LANG = {
    'eng': 'English', 'fas': 'Persian', 'per': 'Persian', 'fra': 'French', 'fre': 'French', 'deu': 'German', 'ger': 'German',
    'spa': 'Spanish', 'ita': 'Italian', 'rus': 'Russian', 'ara': 'Arabic', 'tur': 'Turkish', 'jpn': 'Japanese', 'kor': 'Korean',
    'zho': 'Chinese', 'chi': 'Chinese', 'hin': 'Hindi', 'por': 'Portuguese', 'nld': 'Dutch', 'dut': 'Dutch', 'swe': 'Swedish',
    'pol': 'Polish', 'heb': 'Hebrew', 'ell': 'Greek', 'gre': 'Greek', 'dan': 'Danish', 'nor': 'Norwegian', 'fin': 'Finnish',
    'ces': 'Czech', 'cze': 'Czech', 'hun': 'Hungarian', 'ron': 'Romanian', 'rum': 'Romanian', 'ukr': 'Ukrainian',
    'fa': 'Persian', 'en': 'English',
}
EXTRA_PLACES = [
    r'%LOCALAPPDATA%\Microsoft\WinGet\Packages\*FFmpeg*\**\bin\ffprobe.exe',
    r'%LOCALAPPDATA%\Microsoft\WinGet\Links\ffprobe.exe',
    r'C:\ffmpeg\bin\ffprobe.exe', r'C:\Program Files\ffmpeg\bin\ffprobe.exe',
]


def find_ffprobe(settings=None):
    """The ffprobe to use: the path set in Settings, one on PATH, or a usual install place. None if not found."""
    custom = (settings or {}).get('ffprobe_path') or ''
    if custom and os.path.isfile(custom):
        return custom
    found = shutil.which('ffprobe')
    if found:
        return found
    for pattern in EXTRA_PLACES:
        hits = glob.glob(os.path.expandvars(pattern), recursive=True)
        if hits:
            return hits[0]
    return None


def lang_name(code):
    code = (code or '').strip().lower()
    return None if code in ('', 'und', 'zxx', 'mis', 'mul') else LANG.get(code, code.upper())


def parse_probe(js):
    """ffprobe -print_format json output -> {duration (seconds), width, height, vcodec, bitrate, audio [names], subs [names]}."""
    streams = js.get('streams') or []
    video = next((s for s in streams if s.get('codec_type') == 'video' and not (s.get('disposition') or {}).get('attached_pic')), {})
    fmt = js.get('format') or {}

    def names(kind):
        out = []
        for s in streams:
            if s.get('codec_type') == kind:
                n = lang_name((s.get('tags') or {}).get('language'))
                if n and n not in out:
                    out.append(n)
        return out
    try:
        duration = int(float(fmt.get('duration') or video.get('duration') or 0)) or None
    except (TypeError, ValueError):
        duration = None
    try:
        bitrate = int(fmt.get('bit_rate') or 0) or None
    except (TypeError, ValueError):
        bitrate = None
    return {'duration': duration, 'width': video.get('width'), 'height': video.get('height'), 'vcodec': video.get('codec_name'),
            'bitrate': bitrate, 'audio': names('audio'), 'subs': names('subtitle'),
            'n_audio': sum(1 for s in streams if s.get('codec_type') == 'audio'),
            'n_subs': sum(1 for s in streams if s.get('codec_type') == 'subtitle')}


def probe_file(ffprobe, path, timeout=45):
    """Probe one file. Returns the parsed dict, or None when ffprobe could not read it."""
    flags = 0x08000000 if os.name == 'nt' else 0  # CREATE_NO_WINDOW: no console flashes from the windowed app
    try:
        out = subprocess.run([ffprobe, '-v', 'error', '-print_format', 'json', '-show_format', '-show_streams', path],
                             capture_output=True, timeout=timeout, creationflags=flags)
        if out.returncode:
            return None
        return parse_probe(json.loads(out.stdout.decode('utf-8', 'replace')))
    except (OSError, subprocess.SubprocessError, ValueError):
        return None


def real_height_class(width, height):
    """The resolution a picture size really is: '2160p' | '1080p' | '720p' | '576p' | 'SD'. Width decides, because
    wide films are cropped (1920x800 is 1080p)."""
    w = width or 0
    if w >= 3000:
        return '2160p'
    if w >= 1800:
        return '1080p'
    if w >= 1200:
        return '720p'
    if w >= 900:
        return '576p'
    return 'SD' if w else None
