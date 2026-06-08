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

}