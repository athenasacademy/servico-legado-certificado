﻿using AthenasAcademy.GeradorCertificado.Dominio.Requests;
using AthenasAcademy.GeradorCertificado.Dominio.Responses;
using AthenasAcademy.GeradorCertificado.Models;
using AthenasAcademy.GeradorCertificado.Services.Interfaces;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PdfSharp.Pdf;
using PdfSharp.Drawing;

namespace AthenasAcademy.GeradorCertificado.Services
{
    public class GeradorCertificadoService : IGeradorCertificadoService
    {
        #region Dependencias
        private IGerenciadorArquivosService gerenciadorArquivosService = GerenciadorArquivosService.Instancia;
        private IQRCodeService qrCodeService = QRCodeService.Instancia;
        private IPNGService pngService = PNGService.Instancia;
        #endregion

        #region Contrutores
        private static GeradorCertificadoService instancia;

        public static GeradorCertificadoService Instancia
        {
            get {
                if (instancia is null)
                    instancia = new GeradorCertificadoService();

                return instancia; 
            }
        }

        private GeradorCertificadoService()
        {

        }
        #endregion

        #region Métodos públicos
        public async Task<NovoCertificadoPDFResponse> GerarCertificadoPDF (NovoCertificadoRequest request)
        {
            gerenciadorArquivosService.LimparCaminhoBase();

            string nomeArquivoPNG = "CERTIFICADO_" + request.Matricula.ToString("D10") + ".png";
            string nomeArquivoQRCode = "QRCODE_" + request.Matricula.ToString("D10") + ".png";
            string nomeArquivoPDF = "CERTIFICADO_" + request.Matricula.ToString("D10") + ".pdf";

            string textoCertificado = GerarTexto(request);

            QrCodeDetalhesModel qrCodeDetalhesModel = qrCodeService.GerarQRCode(
                nomeArquivoQRCode, request.Matricula, request.CodigoCurso, gerenciadorArquivosService.RecuperarCaminhoBase());
            
            PNGDetalhesModel pngDetalhesModel = pngService.GerarPNG(
                nomeArquivoPNG, qrCodeDetalhesModel.CaminhoArquivo, textoCertificado, 
                gerenciadorArquivosService.RecuperarCaminhoArquivo(nomeArquivoPNG, exception: false), gerenciadorArquivosService.RecuperarCaminhoArquivoModelo());
            
            PDFDetalhesModel pdfDetalhesModel = GerarPDF(nomeArquivoPDF, pngDetalhesModel.CaminhoArquivo);

            GerarNovoCertificadoModel novoCertificado = new GerarNovoCertificadoModel()
            {
                QrCode = qrCodeDetalhesModel,
                PNG = pngDetalhesModel,
                PDF = pdfDetalhesModel,
            };

            return await Task.FromResult(
                new NovoCertificadoPDFResponse()
                {
                    Arquivo = novoCertificado.PDF.NomeArquivo,
                    CertificadoPDF = novoCertificado.PDF.CaminhoArquivo,
                    CertificadoBytes = File.ReadAllBytes(novoCertificado.PDF.CaminhoArquivo),
                    CertificadoBase64 = novoCertificado.PDF.PDFBase64
                });
        }
        #endregion

        #region Métodos Privados
        private PDFDetalhesModel GerarPDF(string nomeArquivoPDF, string caminhoArquivoPng)
        {
            string caminhoArquivoPdf = gerenciadorArquivosService.RecuperarCaminhoArquivo(nomeArquivoPDF, exception: false);

            System.Drawing.Image imagem = System.Drawing.Image.FromFile(caminhoArquivoPng);

            using (PdfDocument document = new PdfDocument())
            {
                try
                {
                    PdfPage page = document.AddPage();
                    page.Width = XUnit.FromPoint(imagem.Width);
                    page.Height = XUnit.FromPoint(imagem.Height);

                    XGraphics gfx = XGraphics.FromPdfPage(page);
                    XImage image = XImage.FromFile(caminhoArquivoPng);

                    double x = (page.Width - imagem.Width) / 2;
                    double y = (page.Height - imagem.Height) / 2;

                    gfx.DrawImage(image, x, y, imagem.Width, imagem.Height);
                    document.Save(caminhoArquivoPdf);
                }
                catch (IOException ex)
                {
                    throw ex;
                }
            }

            return new PDFDetalhesModel
            {
                PDFBase64 = gerenciadorArquivosService.ConverterParaBase64(caminhoArquivoPdf),
                NomeArquivo = Path.GetFileName(caminhoArquivoPdf),
                CaminhoArquivo = caminhoArquivoPdf,
                TamanhoArquivo = gerenciadorArquivosService.ObterTamanhoArquivo(caminhoArquivoPdf)
            };
        }

        private string FormatarDetahesAluno(string nomeAluno)
        {
            TextInfo textInfo = new CultureInfo("en-US", false).TextInfo;
            nomeAluno = textInfo.ToTitleCase(nomeAluno.ToLower());

            string[] sNomes = nomeAluno.Split(' ');

            if (sNomes.Count() == 0)
                return sNomes[0];

            for (int i = 0; i < sNomes.Count(); i++)
            {
                sNomes[i] = RemoverParticulas(sNomes[i]);
            }

            for (int i = 0; i < sNomes.Count() - 1; i++)
            {
                if (i > 0)
                    sNomes[i] = AbreviarSobrenome(sNomes[i]);
            }

            sNomes = sNomes.Where(s => !string.IsNullOrEmpty(s)).ToArray();

            return string.Join(" ", sNomes);
        }

        private string FormatarDetahesMatriculaAluno(int matriculaAluno)
        {
            return string.Format("Matrícula: {0}", matriculaAluno.ToString("D8"));
        }

        public string RemoverParticulas(string sobrenome)
        {
            string[] particulas = { "De", "Dos", "Da", "Das" };

            foreach (string particula in particulas)
            {
                if (sobrenome.Equals(particula, StringComparison.OrdinalIgnoreCase))
                    sobrenome = string.Empty;
            }

            return sobrenome;
        }

        public string AbreviarSobrenome(string sobrenome)
        {
            if (sobrenome.Length > 0)
                sobrenome = $"{sobrenome[0]}.";

            return sobrenome;
        }

        private string FormatarDetahesCurso(string nomeCurso, int codCurso, DateTime dataConclusao, int cargaHoraria)
        {
            nomeCurso = nomeCurso.ToUpper();
            string cargaHorariaCurso = cargaHoraria.ToString() + " horas";
            string codigoCurso = codCurso.ToString("D6");

            if (nomeCurso.Length < 25)
            {
                return string.Format(
                    "concluiu em {0} o curso '{1}' código: {2} com carga horária total de {3}",
                    dataConclusao.ToString("dd/MM/yyyy"),
                    nomeCurso,
                    codigoCurso,
                    cargaHorariaCurso);
            }

            else if (nomeCurso.Length > 25 && nomeCurso.Length < 50)
            {
                return string.Format(
                    "concluiu em {0} o curso '{1}' código: {2}{3}{4} com carga horária total de {5}",
                    dataConclusao.ToString("dd/MM/yyyy"),
                    nomeCurso,
                    codigoCurso,
                    Environment.NewLine,
                    Environment.NewLine,
                    cargaHorariaCurso);
            }

            else
            {
                return string.Format(
                    "concluiu em {0} o curso '{1}'{2} código: {3} com carga horária total de {4}",
                    dataConclusao.ToString("dd/MM/yyyy"),
                    nomeCurso,
                    Environment.NewLine,
                    codigoCurso,
                    cargaHorariaCurso);
            }
        }

        private string FormatarDetahesAproveitamentoCurso(int taxaAproveitamento) 
        {
            return string.Format("ministrado pela Athenas Academy, onde obteve um aproveitamento de {0}%.", taxaAproveitamento.ToString());
        }

        private string GerarTexto(NovoCertificadoRequest request)
        {
            string msgAluno = FormatarDetahesAluno(request.NomeAluno);
            string msgMatricula = FormatarDetahesMatriculaAluno(request.Matricula);
            string msgCurso = FormatarDetahesCurso(request.NomeCurso, request.CodigoCurso, request.DataConclusao, request.CargaHoraria);
            string msgAproveitamento = FormatarDetahesAproveitamentoCurso(request.Aproveitamento);

            StringBuilder sb = new StringBuilder();

            sb.AppendLine(msgAluno);
            sb.AppendLine();
            sb.AppendLine(msgMatricula);
            sb.AppendLine();
            sb.AppendLine(msgCurso);
            sb.AppendLine();
            sb.AppendLine(msgAproveitamento);

            return sb.ToString();
        }
        #endregion
    }
}
