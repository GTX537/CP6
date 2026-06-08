// FMSE PANEL_EI
package mcframe.clientMABC.MABCP0020;

import java.awt.*;
import java.util.ArrayList;
import java.util.List;
import mcframe.clientCore.beans.action.*;
import mcframe.clientCore.beans.combobox.*;
import mcframe.clientCore.beans.*;
import mcframe.clientCore.event.*;
import mcframe.commonCore.lang.*;
import mcframe.commonCore.notice.*;
import mcframe.commonCore.container.*;
import mcframe.commonCore.container.MCRow.Status;
import mcframe.commonCore.container.MCList.SortType;
import mcframe.clientCore.beans.table.*;
import mcframe.clientCore.beans.tree.*;
import mcframe.clientCore.container.MCConstRow;
import mcframe.clientCore.beans.list.MCLinkBeans;
import mcframe.commonCore.annotation.EI;
import mcframe.clientMAUB.MAUBP5010.*;

import mcframe.commonMA.constant.*;
import mcframe.commonMAUC.MAUCC2010.MAUCC_RowMapperWith;
import mcframe.clientMAUC.MAUCP0040.MAUCP_AppScreenOpener;
import static mcframe.commonMA.notice.MA_Msg.*;
import static mcframe.commonMAUD.MAUDC0120.MAUDC_ListLogicBase.*;
import static mcframe.clientMAUC.MAUCP0010.MAUCP_MCDataUtil.*;
import static mcframe.clientMAUC.MAUCP0020.MAUCP_AppSys.*;

//FMCB IMPORT START

//FMCB IMPORT END

/**
 * 画面"MABCP_CoReg"パネルクラスです。<p>
 *
 * 実装仕様書から自動生成されるクラスです。
 */
@EI
public class MABCP_CoReg extends MABCP_CoReg_MC {

	/**
	 * コンストラクタです。通常の画面として作成するときに使用します。
	 */
	public MABCP_CoReg() {
		super();
	}

	/**
	 * コンストラクタです。埋込カラムとして作成するときに使用します。
	 *
	 * @param name 対応するInnerRow項目名
	 */
	public MABCP_CoReg(String name) {
		super(name);
	}

	public class MA_MABCL0020_Srch01Bridge extends MABCP_CoReg_MC.MA_MABCL0020_Srch01Bridge {
	}
	public class MA_MABCL0020_Upd01Bridge extends MABCP_CoReg_MC.MA_MABCL0020_Upd01Bridge {
	}

//FMCB CUSTOM START
	/**
	 * [MA_MABCL0020_Srch01]の定型処理のInnerClass
	 */
	protected class MA_MABCL0020_Srch01Caller<P extends MABCP_CoReg, B extends MA_MABCL0020_Srch01Bridge> extends MABCP_CoReg_MC.MA_MABCL0020_Srch01Caller<P, B> {

		protected MA_MABCL0020_Srch01Caller(P p, B b) {
			//CUSTOM_MDSOL_CP1_START DEL 2022/12/22 導入時 MDSOL
			//super(p, b);
			//CUSTOM_MDSOL_CP1_END DEL 2022/12/22 導入時 MDSOL

			//CUSTOM_MDSOL_CP1_START ADD 2022/12/22 導入時 MDSOL
			super(x, p, b);
			//CUSTOM_MDSOL_CP1_END ADD 2022/12/22 導入時 MDSOL
		}
	}

	/**
	 * [MA_MABCL0020_Upd01]の定型処理のInnerClass
	 */
	protected class MA_MABCL0020_Upd01Caller<P extends MABCP_CoReg, B extends MA_MABCL0020_Upd01Bridge> extends MABCP_CoReg_MC.MA_MABCL0020_Upd01Caller<P, B> {

		protected MA_MABCL0020_Upd01Caller(P p, B b) {
			super(p, b);
		}
	}
	
	//CUSTOM_MDSOL_CP1_START ADD 2022/12/22 導入時 MDSOL
	/**
	 * 日付を文字列（YYYY/MM/DD）に変換する
	 */
	public String convDate(MCDate date) {
		if(isNull(date)) {
			return null;
		}
		else {
			return String.valueOf(date.getYear()) + "/" + String.valueOf(date.getMonth()) + "/" + String.valueOf(date.getDay());
		}
	}
	//CUSTOM_MDSOL_CP1_END ADD 2022/12/22 導入時 MDSOL

	//CUSTOM_MDSOL_CP1_START DEL 2022/12/22 導入時 MDSOL
	/**
	 * フラグを文字列（0・1）に変換する
	 */
	//public String convFlg(Boolean flg) {
	//	if(flg) {
	//		return "1";
	//	}
	//	else {
	//		return "0";			
	//	}
	//}
	//CUSTOM_MDSOL_CP1_END DEL 2022/12/22 導入時 MDSOL

//FMCB CUSTOM END
}