#!/bin/bash
# ==============================================================================
# 知微 · TeachCraft - macOS 原生打包脚本 (App Bundle + Ad-hoc Sign + DMG)
# ==============================================================================
set -euo pipefail

RID="${1:-osx-arm64}"
VERSION="${2:-1.0.0}"
DEPLOYMENT="${3:-self-contained}"

MODE_LABEL="sc"
SELF_CONTAINED="true"
if [ "$DEPLOYMENT" = "framework-dependent" ]; then
    MODE_LABEL="fdd"
    SELF_CONTAINED="false"
fi

CLEAN_VER="${VERSION#v}"
VER_TAG="v${CLEAN_VER}"
# CFBundleShortVersionString 只接受数字段，预览后缀不写进 Info.plist
PLIST_VER="${CLEAN_VER%%-*}"

echo "=========================================================="
echo "  知微 · TeachCraft - macOS 自动化打包"
echo "  架构: $RID | 模式: $DEPLOYMENT | 版本: $VER_TAG"
echo "=========================================================="

PUBLISH_DIR="dist/TeachCraft-${RID}-${MODE_LABEL}"
PROJECT_PATH="src/TeachCraft.App/TeachCraft.App.csproj"

echo "[1/4] 执行 dotnet publish..."
# macOS 不使用 IncludeNativeLibrariesForSelfExtract（Avalonia 官方文档指出其会导致 CoreCLR 加载冲突）
dotnet publish "$PROJECT_PATH" \
    --configuration Release \
    --runtime "$RID" \
    --self-contained "$SELF_CONTAINED" \
    -p:PublishSingleFile=true \
    -o "$PUBLISH_DIR"

STAGING_DIR="temp/macos-dmg-staging-${RID}-${MODE_LABEL}"
rm -rf "$STAGING_DIR"
mkdir -p "$STAGING_DIR"

APP_DIR="$STAGING_DIR/TeachCraft.app"
CONTENTS_DIR="$APP_DIR/Contents"
MACOS_DIR="$CONTENTS_DIR/MacOS"
RESOURCES_DIR="$CONTENTS_DIR/Resources"

mkdir -p "$MACOS_DIR"
mkdir -p "$RESOURCES_DIR"

echo "[2/4] 组装 TeachCraft.app 应用程序束..."
cp -R "$PUBLISH_DIR"/* "$MACOS_DIR/"

# 清理 .pdb，避免 codesign 将其误判为未签名子代码组件
find "$MACOS_DIR" -type f -name "*.pdb" -delete

# 写入 Info.plist（只替换版本占位符）
sed "s/<string>1.0.0<\/string>/<string>${PLIST_VER}<\/string>/g" bundle/macos/Info.plist > "$CONTENTS_DIR/Info.plist"

printf "APPL????" > "$CONTENTS_DIR/PkgInfo"

if [ -f "src/TeachCraft.App/Assets/teachcraft.icns" ]; then
    cp "src/TeachCraft.App/Assets/teachcraft.icns" "$RESOURCES_DIR/teachcraft.icns"
fi

chmod -R 755 "$APP_DIR"

echo "[3/4] 执行 macOS Ad-hoc 本地自签名..."
find "$MACOS_DIR" -type f -name "*.dylib" -exec codesign --force --timestamp=none --sign - {} +

ENTITLEMENTS="bundle/macos/Entitlements.plist"
if [ -f "$MACOS_DIR/TeachCraft" ]; then
    codesign --force --timestamp=none --entitlements "$ENTITLEMENTS" --sign - "$MACOS_DIR/TeachCraft"
fi

codesign --force --timestamp=none --entitlements "$ENTITLEMENTS" --sign - "$APP_DIR"

echo "[4/4] 制作 DMG 拖拽安装镜像..."
mkdir -p artifacts

ln -s /Applications "$STAGING_DIR/Applications"

DMG_NAME="TeachCraft-${VER_TAG}-${RID}-${MODE_LABEL}.dmg"
DMG_PATH="artifacts/${DMG_NAME}"
rm -f "$DMG_PATH"

hdiutil create -volname "知微 · TeachCraft" \
    -srcfolder "$STAGING_DIR" \
    -ov \
    -format UDZO \
    "$DMG_PATH"

codesign --force --sign - "$DMG_PATH" || true

ZIP_NAME="TeachCraft-${VER_TAG}-${RID}-${MODE_LABEL}.zip"
ZIP_PATH="artifacts/${ZIP_NAME}"
rm -f "$ZIP_PATH"
ditto -c -k --sequesterRsrc --keepParent "$APP_DIR" "$ZIP_PATH"

rm -rf "$STAGING_DIR"

echo ""
echo "=========================================================="
echo "  macOS 打包完成！"
echo "  DMG 产物: $DMG_PATH ($(du -h "$DMG_PATH" | cut -f1))"
echo "  ZIP 产物: $ZIP_PATH ($(du -h "$ZIP_PATH" | cut -f1))"
echo "=========================================================="
