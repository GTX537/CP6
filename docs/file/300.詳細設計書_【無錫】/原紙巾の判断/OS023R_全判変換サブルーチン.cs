using System;

class LDFILE
{
    // DSFAA03P データ構造の宣言
}

class VDTAA
{
    // DSVDTAAP 外部サブルーチンのインターフェース
}

class VDBDAT
{
    // DSVDTBBP 外部サブルーチンのインターフェース
}

class VWEAA
{
    // DSVWEAAP 外部サブルーチンのインターフェース
}

class VWEBB
{
    // DSVWEBBP 外部サブルーチンのインターフェース
}


class PLIST
{
    public string P_VMM { get; set; }  // 責任部門
    public string P_SHK { get; set; }  // 製品区分
    public string P_DAN { get; set; }  // 段
    public string P_GSC1 { get; set; } // 原紙１
    public string P_PRC1 { get; set; } // 印刷１
    public string P_EBC1 { get; set; } // ｴﾝﾎﾞｽ1
    public string P_GSC2 { get; set; } // 原紙２
    public string P_PRC2 { get; set; } // 印刷２
    public string P_EBC2 { get; set; } // ｴﾝﾎﾞｽ2
    public string P_GSC3 { get; set; } // 原紙３
    public string P_PRC3 { get; set; } // 印刷３
    public string P_EBC3 { get; set; } // ｴﾝﾎﾞｽ3
    public string P_HAB1 { get; set; } // 受注幅１
    public string P_NGR1 { get; set; } // 受注流れ１
    public string P_SRY1 { get; set; } // 受注数量１
    public string P_HAB2 { get; set; } // 受注幅２
    public string P_NGR2 { get; set; } // 受注流れ２
    public string P_SRY2 { get; set; } // 受注数量２
    public string P_LIN3 { get; set; } // 表幅
    public string P_HAB3 { get; set; } // 全判幅
    public string P_NGR3 { get; set; } // 全判流れ
    public string P_SRY3 { get; set; } // 全判数量
    public string P_KOK { get; set; }  // ｺﾙｽﾘ区分
    public string P_WRS { get; set; }  // 割数
    public string P_WRSM { get; set; } // 最大割数
    public string P_DANS { get; set; } // 断裁区分
    public string P_BIKS { get; set; } // 製造備考
    public string P_STS { get; set; }  // ステータス
}


class Program
{
    static void Main(string[] args)
    {
        INIT();
        NGR();

        if (P_HAB2 == 0)
        {
            HAB1();
        }
        else
        {
            HAB2();
        }

        if (IX != 0)
        {
            ZENBN();
        }

        END();
    }

    // 初期処理
    void INIT()
    {
        // ワーク
        VWBCHK = "VWB";
        P@VMM += W1VMM;
        W1VMM = CHAINFAF03L01();
        FCVMG = 0;
        W1VMG += FCVMG;
        W1BAIH = 0;
        P@HAB1 += P@HAB2 + W1HABJ;
        W1HAB1 = 0;
        W1HAB2 = 0;
        W1HABA = "";
        W1HABB = "";
        W2HAB1 = 0;
        W1GHAB = 0;
        W1NGR3 = 0;
        W1SNGR = 0;
        if (P@SRY2 == 0)
        {
            W1SRY1 += P@SRY1;
        }
        else
        {
            P@SRY1 += P@SRY2;
        }
        W1LOS = 0;
        W1LOSH = 0;
        W1SA = 0;
        W1WRS = 0;
        W1WRS2 = 0;
        W1WRS3 = 0;
        W1WRSN = 0;
        W1WRSS = 0;
        W1HABF = 0;
        W1BIKS = "";
        W1STS = "";
        IX = 0;
        IY = 0;
        P@WRSM = 0;

        // ＬＤＡ
        // *NAMVAR IN LDFILE;
        // 大阪ローカルＦＬＧ
        // LDVMG SETON 61;

        // データエリア
        // *NAMVAR IN DAA020;
        // Z-ADD DAA020 W1SNGR;

        // 最大倍率取得（何個付けまで求めるか？）
        // *IN90 Z-ADD 3 W1BAIR;

        // 製造設定ファイル参照
        P@VMM += EGVMM;
        if (P@SHK >= 21 && P@SHK <= 23 || P@SHK >= 31 && P@SHK <= 33)
        {
            EGROK = 2;
        }
        else
        {
            EGROK = 1;
        }
        CHAINFAE07L01();
        if (*IN90 == true)
        {
            P@STS = "NEG";
            \END();
        }
        EGKOHS = 2 * W1KOHS;
        EGKOHM = 2 * W1KOHM;
        EGKSSN += W1SNGR;

        ENDSR();
    }

    // 流れ
    void NGR()
    {
        do
        {
            // 流れが最小流れ以上、又は片面ならそのまま
            if (P@NGR1 >= W1SNGR || P@SHK >= 21 || P@SHK <= 23)
            {
                P@NGR1 += W1NGR3;
                W1WRSN = 1;
            }
            else
            {
                // 流れが最小流れ未満なら超えるまでｎ倍
                W1WRSN = 1;
                P@NGR1 += W1NGR3;
                while (W1NGR3 < W1SNGR)
                {
                    W1WRSN += 1;
                    W1NGR3 += P@NGR1;
                }
            }
        } while (false); // ループを1回のみ実行

        // Z-ADD1 P@DANS;  // この行の目的が不明なためコメントアウト

        ENDSR();
    }

    // 通常変換
    void HAB1()
    {
        W2LOSS = 999999; // 最小ロス初期値
        W2LOSH = 999999; // 最小ロス初期値

        // ＜幅＞
        for (int i = 1; i <= W1BAIR; i++)
        {
            W2HAB1 = P@HAB1 * W1BAIH;

            // ｺﾙｽﾘ区分＝化粧裁ちOR化粧半裁ならｺﾙｽﾘ幅加算
            if (P@KOK == 3 || P@KOK == 4)
            {
                W2HAB1 += W1KOHS;
            }

            // 最大幅超えたら終了
            if (W2HAB1 > W1MXHB)
            {
                if (W1BAIH > 1)
                {
                    break;
                }
            }

            // 表原紙幅取得
            if (P@SHK >= 21 && P@SHK <= 23)
            {
                // 名古屋ローカル
                W1GSC = P@GSC1;
                W1HAB1 = P@HAB1;
                W1HABF = 1;
                GHAB();

                if (W1GHAB == 0)
                {
                    continue;
                }
            }

            // 裏原紙幅取得
            W1GSC = P@GSC3;
            W1HAB1 = P@HAB1;
            W1HABF = 0;
            GHAB();

            if (W1GHAB == 0)
            {
                continue;
            }

            // ロス算出
            W2HABJ = W1BAIH * W1HABJ;
            W1LOS = W1GHAB - W2HABJ;

            // ロス
            W1DEC = W1SRY1 / W1BAIH;
            W1LOS *= W1NGR3;
            W1LOS /= 1000000;
            W1LOSH = W2SRY1 * W1LOSH;

            // 前回まで最小ロスと比較
            if (W1LOSH <= W2LOSH)
            {
                // 対象寸法をセット
                IX += 1;
                WRS[IX] = W1BAIH;
                HAB[IX] = W1GHAB;
                LOS[IX] = W1LOS;
                W2LOSS = W1LOS;
                W2LOSH = W1LOSH;
            }
        }

        ENDSR();
    }

    static void HAB2()
    {
        do
        {
            W2HAB1 = W1HABJ + W2HAB1;

            // ｺﾙｽﾘ区分＝化粧裁ちOR化粧半裁ならｺﾙｽﾘ幅加算
            if (P_KOK == 3 || P_KOK == 4)
            {
                W2HAB1 += W1KOHS;
            }

            // 最大幅超えたら終了
            if (W2HAB1 > W1MXHB)
            {
                break;
            }

            // 原紙幅取得
            W1GSC = P_GSC3;
            W1HAB1 = W2HAB1;
            W1HABF = 0;

            GHAB();

            if (W1GHAB == 0)
            {
                break;
            }

            // ロス算出
            W1GHAB -= W1HABJ;
            W2LOSS = W1LOS;

            // 対象寸法を配列にセット
            for (int i = 0; i < 20; i++)
            {
                WRS[i] = 2;
                HAB[i] = W1GHAB;
                LOS[i] = W1LOS;
            }
        }
        while (true);
    }

    // 全判寸法SET
    void ZENBN()
    {
        // 優先順位
        // １．ロスが少ないもの
        // ２．割数２があれば２
        // ３．割数３があれば３
        // ４．割数３以上は最大割数を選択

        // 最小・最大割数探索、合わせて割数＝２の有無チェック
        int W1WRS2 = 0; // 割数２有無
        int W1WRS3 = 0; // 割数３有無
        int W1WRSS = 99; // 最小割数初期値
        int W2WRSM = 0; // 最大割数初期値

        for (int i = 1; i <= IX; i++)
        {
            if (W2LOSS == LOS[i])
            {
                if (WRS[i] == 3)
                {
                    // 大阪対象外
                    continue;
                }

                if (WRS[i] == 2)
                {
                    W1WRS2 = 1;
                }

                if (WRS[i] < W1WRSS)
                {
                    W1WRSS = WRS[i];
                }

                if (WRS[i] > W2WRSM)
                {
                    W2WRSM = WRS[i];
                }
            }
        }

        // ロス有りも含む選択出来る最大巾
        if (WRS[i] > P@WRSM)
        {
            P@WRSM = WRS[i];
        }

        // 最小ロスが３つ割以上なら２つ割り優先
        if (W1WRSS >= 3)
        {
            if (WKYUS == 3)
            {
                // その他拠点
                W1WRS = 3;
            }
            else if (WKYUS == 2)
            {
                // 大阪ローカル
                W1WRS = 2;
            }
            else
            {
                W1WRS = 1;
            }

            for (int i = 1; i <= IX; i++)
            {
                if (WRS[i] <= WKYUS)
                {
                    if (WRS[i] <= 2)
                    {
                        W1WRS = 2;
                    }
                    else
                    {
                        W1WRS = 1;
                    }
                }
            }
        }

        // 最小割数が1 OR 2。２が優先
        if (W1WRSS <= WKYUS)
        {
            if (W1WRS3 == 1)
            {
                W1WRS = 3;
            }
            else if (W1WRS2 == 1)
            {
                W1WRS = 2;
            }
            else
            {
                W1WRS = 1;
            }
        }

        // 最小割数が３以上なら最大割数
        if (W1WRSS >= 3)
        {
            W1WRS = W2WRSM;
        }

        IY = 1;
        while (*IN90 != *ON)
        {
            // 対象パターン確定
            P@HAB3 = HAB[IY];
            P@NGR3 = W1NGR3;
            P@WRS = WRS[IY];

            // 割数より数量算出
            if (P@WRS <= 1)
            {
                W1SRY = W1SRY1;
            }
            else
            {
                W1SRY = W1SRY1 / P@WRS;
            }

            // 表幅
            if (P@GSC1 != *BLANK && P@GSC1 != "0000")
            {
                W1GSC = P@GSC1;
                W1HAB1 = HAB[IY];
                W1HABF = 1;
                GHAB();
                if (W1GHAB > HAB[IY])
                {
                    P@LIN3 = W1GHAB;
                }
            }

            // ｺﾙｽﾘ区分判定
            KOK();

            // ローカル削除
            // 名古屋ローカル
            // 製造備考にｽﾘｯﾀｰ幅SET
            // 片面巻ボール判定
            // 24桁で切断

            IY += 1;
        }

        ENDSR();
    }


    // 原紙巾マスタより原紙幅取得
    void GHAB()
    {
        // DPVMGをW1VMGに代入
        int W1VMG = DPVMG;

        // W1GHABに0を代入
        int W1GHAB = 0;

        // SVHAB1をW1HAB1に代入
        int W1HAB1 = SVHAB1;

        // ループ処理
        while (true)
        {
            // 受注幅の直近幅を取得
            DPGSC = W1GSC;
            DPHAB = W1HAB1;
            SETLLFAD16L01();
            READEFAD16L01();
            if (*IN90 == *ON)
            {
                LEAVE;
            }

            // 対象幅チェック
            if (DPJBZK == 1)
            {
                W1HAB1 += 1;
                continue;
            }

            // 芯原紙幅チェック
            if (P@GSC2 != *BLANK && P@GSC2 != "0000" && (W1GSC & P@GSC3) != 0)
            {
                DPGSC = P@GSC2;
                DPHAB += 1;
                CHAINFAD16L01();
                W1HAB1 += 1;
                continue;
            }

            // 見つかった原紙幅と受注幅がイコールなら対象幅
            if (DPHAB == W1HAB1)
            {
                W1GHAB = DPHAB;
                LEAVE;
            }

            // コルスリ最小・最大幅チェック
            if (DPHAB - W1HAB1 > W1SA || DPHAB - SVHAB1 > W1SA)
            {
                // コルスリ区分の事前入力があれば最低コルスリチェックは行わない
                if (P@KOK == 3 || P@KOK == 4)
                {
                    W1HABF = 1;
                }

                if (W1HABF == 0 && W1SA < W1KOHS || W1SA > W1KOHM)
                {
                    W1HAB1 += 1;
                    continue;
                }
            }

            // 対象
            W1GHAB = W1GHAB;
            LEAVE;
        }
    }


    // コルスリ判断
    void KOK()
    {
        // W1KOKに0を代入
        int W1KOK = 0;

        // P@HAB3をP@HAB1で割り、商をAMARIに代入
        int AMARI = P@HAB3 / P@HAB1;

        // 半裁
        if (SHO == 2)
        {
            // 余りが0の場合
            if (AMARI == 0)
            {
                // 表巾が0の場合
                if (P@LIN3 == 0)
                {
                    // W1KOKに2を代入（半裁）
                    W1KOK = 2;
                }
                // 表巾がある場合
                else
                {
                    // W1VMが6または9の場合
                    if (W1VM == 6 || W1VM == 9)
                    {
                        // W1KOKに2を代入（半裁）
                        W1KOK = 2;
                    }
                    // それ以外の場合
                    else
                    {
                        // W1KOKに4を代入（化粧半裁）
                        W1KOK = 4;
                    }
                }
            }
            // 余りがある場合
            else
            {
                // コルスリ可？
                if (W2LOSS >= W1KOHS && W2LOSS <= W1KOHM)
                {
                    // W1KOKに4を代入（化粧半裁）
                    W1KOK = 4;
                }
                else
                {
                    // W1KOKに0を代入（ナシ）
                    W1KOK = 0;
                }
            }
        }
        // 半裁以外
        else
        {
            // 一面？
            if (SHO == 1)
            {
                // 流カット分？
                if (W2LOSS == 0 && P@LIN3 == 0)
                {
                    // W1KOKに0を代入（ナシ）
                    W1KOK = 0;
                }
                else
                {
                    // W1KOKに1を代入（耳）
                    W1KOK = 1;
                }
            }
            else
            {
                // 変則半裁（スリ３有り）？
                if (P@HAB2 != 0)
                {
                    // 表巾が0の場合
                    if (P@LIN3 == 0)
                    {
                        // W1KOKに0を代入（ナシ）
                        W1KOK = 0;
                    }
                    else
                    {
                        // W1KOKに3を代入（化粧裁）
                        W1KOK = 3;
                    }
                }
                else
                {
                    // コルスリ可？
                    if (W2LOSS >= W1KOHS && W2LOSS <= W1KOHM)
                    {
                        // W1KOKに3を代入（化粧裁）
                        W1KOK = 3;
                    }
                    else
                    {
                        // 表巾が0の場合
                        if (P@LIN3 == 0)
                        {
                            // W1KOKに0を代入（ナシ）
                            W1KOK = 0;
                        }
                        // 表巾がある場合
                        else
                        {
                            // W1KOKに1を代入（耳落し）
                            W1KOK = 1;
                        }
                    }
                }
            }
        }
    }

}


