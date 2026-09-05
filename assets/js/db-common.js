// ==========================================================================
// 養成天堂 - 資料庫全域通用工具與客服組件 (DB Common & CS Widget)
// ==========================================================================

const GAS_WEBAPP_URL = "https://script.google.com/macros/s/AKfycbyb55bX3x7e3qY0_x_Qd9t8aP1p-demo-lin/exec";

// 取得機率視覺標籤
function getRateBadge(p) {
  let cls = "badge rate-mid";
  let txt = p + "%";
  if (p >= 50) {
    cls = "badge rate-high";
  } else if (p >= 10) {
    cls = "badge rate-mid";
  } else if (p >= 1) {
    cls = "badge rate-low";
  } else if (p >= 0.1) {
    cls = "badge rate-rare";
    txt = "★ " + p + "%";
  } else {
    cls = "badge rate-legend";
    txt = "👑 " + p + "% (傳奇)";
  }
  return `<span class="${cls}">${txt}</span>`;
}

// 抽屜模態框控制
function openDrawer(titleHtml, bodyHtml) {
  let overlay = document.getElementById("db-drawer-overlay");
  if (!overlay) {
    overlay = document.createElement("div");
    overlay.id = "db-drawer-overlay";
    overlay.className = "db-drawer-overlay";
    overlay.innerHTML = `
      <div class="db-drawer">
        <div class="db-drawer-header">
          <div class="db-drawer-title" id="db-drawer-title"></div>
          <button class="db-drawer-close" onclick="closeDrawer()">✕</button>
        </div>
        <div class="db-drawer-body" id="db-drawer-body"></div>
      </div>
    `;
    overlay.addEventListener("click", function(e) {
      if (e.target === overlay) closeDrawer();
    });
    document.body.appendChild(overlay);
  }
  document.getElementById("db-drawer-title").innerHTML = titleHtml;
  document.getElementById("db-drawer-body").innerHTML = bodyHtml;
  overlay.classList.add("open");
}

function closeDrawer() {
  const overlay = document.getElementById("db-drawer-overlay");
  if (overlay) overlay.classList.remove("open");
}

// 懸浮客訴對話框
function toggleCsModal() {
  const modal = document.getElementById("cs-modal-box");
  if (!modal) return;
  modal.classList.toggle("open");
  if (modal.classList.contains("open")) {
    const input = document.getElementById("cs-name");
    if (input && !input.value) input.focus();
  }
}

async function handleCsSubmit(e) {
  e.preventDefault();
  const nameEl = document.getElementById("cs-name");
  const contactEl = document.getElementById("cs-contact");
  const contentEl = document.getElementById("cs-content");
  const container = document.getElementById("cs-messages-container");
  const sendBtn = document.getElementById("cs-send-btn");

  const name = nameEl.value.trim();
  const contact = contactEl.value.trim() || "未提供";
  const content = contentEl.value.trim();
  if (!name || !content) return;

  // 立即在前端顯示發送氣泡
  const timeStr = new Date().toLocaleTimeString("zh-TW", { hour: '2-digit', minute: '2-digit' });
  const userBubble = document.createElement("div");
  userBubble.className = "cs-msg user";
  userBubble.innerHTML = `
    <div>${content}</div>
    <div class="cs-msg-meta" style="color:rgba(0,0,0,0.6)">${name} ｜ ${timeStr}</div>
  `;
  container.appendChild(userBubble);
  container.scrollTop = container.scrollHeight;

  // 清空輸入框
  contentEl.value = "";
  sendBtn.disabled = true;
  sendBtn.innerText = "發送中...";

  // 模擬/真實推送至 Google 試算表【客訴聊天】分頁
  const payload = {
    action: "cs_chat",
    name: name,
    contact: contact,
    content: content,
    page: window.location.pathname.split("/").pop() || "首頁"
  };

  try {
    fetch(GAS_WEBAPP_URL, {
      method: "POST",
      mode: "no-cors",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(payload)
    }).catch(err => console.log("GAS log push:", err));
  } catch(err) {}

  setTimeout(() => {
    sendBtn.disabled = false;
    sendBtn.innerText = "發送 ✈️";

    const autoReply = document.createElement("div");
    autoReply.className = "cs-msg gm";
    autoReply.innerHTML = `
      <div>🔔 <b>客服系統通知：</b> 感謝勇者 <b>${name}</b> 的反饋！您的客訴內容已<b>自動推播至 GM 營運後台 Google 試算表【客訴聊天】分頁</b>。客服主管排查中，請稍候。</div>
      <div class="cs-msg-meta">🎧 在線值班 GM ｜ 剛剛</div>
    `;
    container.appendChild(autoReply);
    container.scrollTop = container.scrollHeight;
  }, 700);
}

// 監聽 ESC 關閉模態框
document.addEventListener("keydown", function(e) {
  if (e.key === "Escape") {
    closeDrawer();
    const modal = document.getElementById("cs-modal-box");
    if (modal && modal.classList.contains("open")) modal.classList.remove("open");
  }
});
