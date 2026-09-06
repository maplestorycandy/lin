/**
 * ==============================================================================
 * 天堂ARPG 官方網站 - Google Apps Script (GAS) 後端串接腳本
 * ==============================================================================
 * 
 * 📌 目標試算表：
 * https://docs.google.com/spreadsheets/d/1ggRTUJFgjEeXd-Nz8O0CDiZE7gSjb9GbnqlakPRgbSQ/edit?gid=0#gid=0
 * 
 * 📌 功能特點：
 * 1. 自動建立「玩家留言」分頁（若不存在），欄位：時間戳記、角色名稱、職業、建議類別、留言內容、狀態
 * 2. 自動建立「客訴聊天」分頁（若不存在），欄位：時間戳記、角色暱稱、聯絡方式、客訴內容、處理狀態
 * 3. 支援 POST 與 GET 跨域請求 (CORS-friendly)，無論使用 fetch 或 ajax 都能完美接收與寫入
 * 4. 支援查詢 API：可透過 GET 取得「玩家留言」即時清單回顯於官網
 * 
 * 📌 部署教學（超簡單 3 步驟）：
 * 1. 開啟上方 Google 試算表，點選上方選單：「擴充功能 (Extensions)」 -> 「Apps Script」
 * 2. 清空編輯器預設代碼，將此檔案的所有內容複製貼上，點擊上方 💾 儲存按鈕
 * 3. 點選右上角藍色「部署 (Deploy)」 -> 「新增部署作業 (New deployment)」
 *    - 齒輪圖示選擇：「網路應用程式 (Web app)」
 *    - 說明 (Description)：天堂ARPG 留言與客訴 API
 *    - 執行身分 (Execute as)：我 (Me - 您的 Google 帳號)
 *    - 誰可以存取 (Who has access)：所有人 (Anyone)  <--- ⚠️ 重要！一定要選「所有人」
 *    - 點擊「部署」，授權存取權限
 *    - 複製產生的「網路應用程式網址 (Web app URL)」(以 /exec 結尾)
 *    - 貼入 index.html 中的 APPS_SCRIPT_URL 變數即可！
 * ==============================================================================
 */

// 試算表 ID（已預設為您的試算表）
const SPREADSHEET_ID = '1ggRTUJFgjEeXd-Nz8O0CDiZE7gSjb9GbnqlakPRgbSQ';

/**
 * 取得或建立指定的試算表分頁 (Sheet Tab)
 */
function getOrCreateSheet(sheetName, headers) {
  let ss;
  try {
    ss = SpreadsheetApp.getActiveSpreadsheet();
    if (!ss) {
      ss = SpreadsheetApp.openById(SPREADSHEET_ID);
    }
  } catch (err) {
    ss = SpreadsheetApp.openById(SPREADSHEET_ID);
  }

  let sheet = ss.getSheetByName(sheetName);
  if (!sheet) {
    sheet = ss.insertSheet(sheetName);
    // 設定表頭
    if (headers && headers.length > 0) {
      const headerRange = sheet.getRange(1, 1, 1, headers.length);
      headerRange.setValues([headers]);
      headerRange.setFontWeight('bold');
      headerRange.setBackground('#2b394a');
      headerRange.setFontColor('#f1d98c');
      sheet.setFrozenRows(1);
    }
  }
  return sheet;
}

/**
 * 處理 POST 請求（接收官網送出的留言或客訴）
 */
function doPost(e) {
  try {
    let data = {};
    if (e.postData && e.postData.contents) {
      try {
        data = JSON.parse(e.postData.contents);
      } catch (parseErr) {
        data = e.parameter || {};
      }
    } else if (e.parameter) {
      data = e.parameter;
    }

    const type = data.type || 'board'; // 'board' (玩家留言) 或 'chat' (客訴聊天)
    const nowStr = Utilities.formatDate(new Date(), 'Asia/Taipei', 'yyyy-MM-dd HH:mm:ss');

    if (type === 'chat' || type === 'complaint') {
      // 處理「客訴聊天」
      const sheet = getOrCreateSheet('客訴聊天', ['時間戳記', '角色暱稱', '聯絡方式', '客訴內容', '處理狀態']);
      const name = data.name || data.nickname || '無名玩家';
      const contact = data.contact || data.phone || '未提供';
      const message = data.message || data.content || '';
      const status = '待處理 (新客訴)';

      sheet.appendRow([nowStr, name, contact, message, status]);

      return ContentService.createTextOutput(JSON.stringify({
        status: 'success',
        type: 'chat',
        message: '客訴訊息已成功記錄至試算表！',
        timestamp: nowStr
      })).setMimeType(ContentService.MimeType.JSON);

    } else {
      // 處理「玩家留言」
      const sheet = getOrCreateSheet('玩家留言', ['時間戳記', '角色名稱', '職業', '建議類別', '留言內容', '審核狀態']);
      const name = data.name || '熱心玩家';
      const job = data.job || '未填寫';
      const category = data.category || '遊戲建議';
      const message = data.message || data.content || '';
      const status = '已公開';

      sheet.appendRow([nowStr, name, job, category, message, status]);

      return ContentService.createTextOutput(JSON.stringify({
        status: 'success',
        type: 'board',
        message: '玩家留言已成功存入試算表！',
        timestamp: nowStr
      })).setMimeType(ContentService.MimeType.JSON);
    }

  } catch (error) {
    return ContentService.createTextOutput(JSON.stringify({
      status: 'error',
      message: error.toString()
    })).setMimeType(ContentService.MimeType.JSON);
  }
}

/**
 * 處理 GET 請求（可用於測試或讀取留言列表）
 */
function doGet(e) {
  try {
    const params = e ? e.parameter : {};
    const action = params.action || 'ping';

    // 支援直接透過 GET 新增資料（防止某些瀏覽器限制跨域 POST）
    if (action === 'add_board' || action === 'add_chat') {
      const isChat = (action === 'add_chat');
      const mockPost = {
        parameter: {
          type: isChat ? 'chat' : 'board',
          name: params.name,
          job: params.job,
          category: params.category,
          contact: params.contact,
          message: params.message
        }
      };
      return doPost(mockPost);
    }

    // 讀取「玩家留言」最新清單
    if (action === 'get_board') {
      const sheet = getOrCreateSheet('玩家留言', ['時間戳記', '角色名稱', '職業', '建議類別', '留言內容', '審核狀態']);
      const rows = sheet.getDataRange().getValues();
      const list = [];
      // 從第二行開始讀取（第一行為標題）
      for (let i = 1; i < rows.length; i++) {
        const row = rows[i];
        if (row[0] || row[1] || row[4]) {
          list.push({
            time: row[0],
            name: row[1],
            job: row[2],
            category: row[3],
            message: row[4],
            status: row[5]
          });
        }
      }
      return ContentService.createTextOutput(JSON.stringify({
        status: 'success',
        data: list.reverse() // 最新留言排前面
      })).setMimeType(ContentService.MimeType.JSON);
    }

    // 讀取「官方公告」最新清單（支援隨時於試算表編輯更新）
    if (action === 'get_news') {
      const sheet = getOrCreateSheet('官方公告', ['發布日期', '標籤', '公告標題', '公告內容', '顯示狀態']);
      const rows = sheet.getDataRange().getValues();
      
      // 若試算表剛建立或只有表頭，自動填充預設公告
      if (rows.length <= 1) {
        const defaultNews = [
          ['2026-09-06', '最新', '【核心與戰鬥重大更新】', '妖精「三重矢」0.15s極速CD且施法可同步普攻、後台地圖怪物刷新率即時調整、自訂地圖怪物全數現身(解除BOSS互斥與視野外限制)、原地站立自動刷新重生、核心代碼混淆加密。', '公開'],
          ['2026-09', '更新', '【圖資全同步】', '提取 1,223 隻怪物透明立繪、264 張獵場實景、3,000+ 道具裝備技能原版圖標。', '公開'],
          ['2026-09', '發布', '【更新檔發布】', '上線 Google Drive 最新補丁下載、整合官網留言板與右下角客訴聊天。', '公開'],
          ['2026-09', '核心', '【核心重大更新】', '整合物品掉落過濾器、修復法師45試煉瑪娜斗篷、解除防具詞綴限制。', '公開'],
          ['2026-08', '活動', '【福利商城上線】', '奇岩 1 元福利商人、白/祝/詛武防卷全面上架。', '公開'],
          ['2026-08', '優化', '【環境優化】', '日夜交替黑夜恢復、妖精夜視功能加入（可自由切換）。', '公開']
        ];
        sheet.getRange(2, 1, defaultNews.length, 5).setValues(defaultNews);
        try { sheet.autoResizeColumns(1, 5); } catch(e){}
        return ContentService.createTextOutput(JSON.stringify({
          status: 'success',
          data: defaultNews.map(function(r) {
            return {
              date: r[0],
              tag: r[1],
              title: r[2],
              content: r[3],
              status: r[4]
            };
          })
        })).setMimeType(ContentService.MimeType.JSON);
      }

      const list = [];
      for (let i = 1; i < rows.length; i++) {
        const row = rows[i];
        if (row[0] || row[2] || row[3]) {
          const status = String(row[4] || '公開').trim();
          if (status !== '隱藏' && status !== '草稿') {
            let dateStr = row[0];
            if (row[0] instanceof Date) {
              dateStr = Utilities.formatDate(row[0], 'Asia/Taipei', 'yyyy-MM-dd');
            } else {
              dateStr = String(row[0] || '').trim();
            }
            list.push({
              date: dateStr,
              tag: String(row[1] || '公告').trim(),
              title: String(row[2] || '').trim(),
              content: String(row[3] || '').trim(),
              status: status
            });
          }
        }
      }
      return ContentService.createTextOutput(JSON.stringify({
        status: 'success',
        data: list
      })).setMimeType(ContentService.MimeType.JSON);
    }

    // 預設健康狀態回傳
    return ContentService.createTextOutput(JSON.stringify({
      status: 'online',
      service: '天堂ARPG 官方網站 API',
      spreadsheetId: SPREADSHEET_ID,
      time: Utilities.formatDate(new Date(), 'Asia/Taipei', 'yyyy-MM-dd HH:mm:ss')
    })).setMimeType(ContentService.MimeType.JSON);

  } catch (error) {
    return ContentService.createTextOutput(JSON.stringify({
      status: 'error',
      message: error.toString()
    })).setMimeType(ContentService.MimeType.JSON);
  }
}
