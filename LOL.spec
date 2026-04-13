# -*- mode: python ; coding: utf-8 -*-


a = Analysis(
    ['red_black_detector.py'],
    pathex=[],
    binaries=[],
    datas=[('lol.ico', '.'), ('Darkmode demo-Regular.ttf', '.'), ('on.mp3', '.'), ('off.mp3', '.')],
    hiddenimports=['pyautogui', 'mss', 'numpy', 'keyboard', 'cv2', 'pyscreeze', 'tkinter', 'pygame'],
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=[],
    noarchive=False,
    optimize=0,
)
pyz = PYZ(a.pure)

exe = EXE(
    pyz,
    a.scripts,
    a.binaries,
    a.datas,
    [],
    name='lol',
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=True,
    upx_exclude=[],
    runtime_tmpdir=None,
    console=False,
    disable_windowed_traceback=False,
    argv_emulation=False,
    target_arch=None,
    codesign_identity=None,
    entitlements_file=None,
    icon=['lol.ico'],
)
